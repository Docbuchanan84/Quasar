using System.Text.Json;
using System.Text.Json.Serialization;
using Lib.Net.Http.WebPush;
using Quasar.Server.Notifications;
using Quasar.Server.Persistence;

namespace Quasar.Server;

public static class QuasarHost
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        var paths = PersistencePaths.Resolve(builder.Environment, builder.Configuration);
        var settings = PersistenceSettings.Read(paths.SettingsFile);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<ICampaignStore>(new SqliteCampaignStore(paths.DataRoot, settings.Autosave.RecoveryCheckpoints));
        builder.Services.AddSingleton(new LabSession(startWithWorld: false));
        builder.Services.AddSingleton(sp => new CampaignLifecycle(sp.GetRequiredService<LabSession>(),
            sp.GetRequiredService<ICampaignStore>(), settings, sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<CampaignLifecycle>>(), paths.DataRoot));
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<CheckpointKind>(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddHostedService<CampaignRunner>();
        builder.Services.AddSingleton(new ArrivalSubscriptionStore(Path.Combine(paths.DataRoot, "arrival-notifications.json")));
        builder.Services.AddHttpClient<PushServiceClient>(client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            })
            .RemoveAllLoggers();
        builder.Services.AddSingleton<ArrivalNotifications>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ArrivalNotifications>());
        builder.Services.AddHostedService<LabRunner>();
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/" || context.Request.Path == "/index.html"
                || context.Request.Path.StartsWithSegments("/torch-designer") || context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            // No cross-origin command access. JSON plus this check prevents ambient-browser form commands.
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (!context.Request.HasJsonContentType() || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    || uri.Authority != context.Request.Host.Value
                    || (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            try { await next(context); }
            catch (CampaignUnavailableException error) { await CampaignEndpoints.Unavailable(error).ExecuteAsync(context); }
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/health", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                application = "Quasar",
                status = "ready",
                stage = "system-lab",
                serverTimeUtc = DateTimeOffset.UtcNow
            });
        });

        app.MapGet("/api/lab", (LabSession session) => session.Snapshot());
        app.MapGet("/api/lab/spacecraft/{shipId}/flight-path", (string shipId, LabSession session, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(session.ForecastFlight(shipId, cancellationToken)); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (Exception error) when (error is InvalidOperationException or ArithmeticException)
            { return Results.Conflict(new { error = "Flight forecast is unavailable from this state." }); }
        });
        app.MapGet("/api/lab/sail-preview", (double? angleDegrees, int? durationSeconds, LabSession session, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(session.PreviewSail(angleDegrees, durationSeconds ?? 0, cancellationToken)); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { return Results.BadRequest(new { error = error.Message }); }
        });
        app.MapGet("/api/lab/course-preview", (string destinationId, int? flightSeconds, double? maneuverBudget,
            string? mode, string? arrivalKind, int? preferredFlightSeconds, double? reservePercent, bool? lockFlightTime,
            LabSession session, CancellationToken cancellationToken) =>
        {
            try
            {
                return mode switch
                {
                    null when arrivalKind is null => Results.Ok(session.PreviewCourse(destinationId, flightSeconds ?? 21600, maneuverBudget)),
                    "auto" when flightSeconds is null => Results.Ok(session.PreviewAutomaticCourse(destinationId, maneuverBudget,
                        cancellationToken, arrivalKind ?? "approach", preferredFlightSeconds, reservePercent, lockFlightTime ?? false)),
                    _ => Results.BadRequest(new { error = "Use automatic mode without specifying a flight duration." })
                };
            }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { return Results.BadRequest(new { error = error.Message }); }
        });
        app.MapGet("/api/lab/trajectory-preview", (string destinationId, int? flightSeconds, LabSession session) =>
        {
            try { return Results.Ok(session.PreviewTravel(destinationId, flightSeconds ?? 21600)); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { return Results.BadRequest(new { error = error.Message }); }
        });
        app.MapGet("/api/lab/target-state", (string kind, string targetId, long atTicks, LabSession session) =>
        {
            try { return Results.Ok(session.TargetStateAt(kind, targetId, atTicks)); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { return Results.BadRequest(new { error = error.Message }); }
        });
        app.MapGet("/api/lab/spacecraft/state", (long atTicks, LabSession session) =>
        {
            try { return Results.Ok(new { atTicks = atTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), state = session.ShipStateAt(atTicks) }); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        });
        app.MapGet("/api/lab/celestial/{bodyId}/state", (string bodyId, long atTicks, LabSession session) =>
        {
            try
            {
                var state = session.CelestialStateAt(bodyId, atTicks);
                // Strings preserve exact ticks for clients whose JSON numbers cannot represent Int64.
                return Results.Ok(new
                {
                    bodyId,
                    atTicks = atTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    state.PositionKm,
                    state.VelocityKmPerSecond
                });
            }
            catch (KeyNotFoundException) { return Results.NotFound(new { error = "Unknown celestial body." }); }
        });
        app.MapPost("/api/lab/commands", (LabCommand command, LabSession session) =>
        {
            try { return Results.Ok(session.Execute(command)); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or OverflowException)
            { return Results.BadRequest(new { error = "Invalid command: " + error.Message }); }
        });

        app.MapCampaignEndpoints();
        app.MapArrivalNotificationEndpoints();
        app.MapTorchDesigner();
        return app;
    }
}
