using Quasar.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LabSession>();
builder.Services.AddHostedService<LabRunner>();
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" || context.Request.Path == "/index.html" || context.Request.Path.StartsWithSegments("/api"))
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
    await next(context);
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
app.MapPost("/api/lab/lease", (LeaseRequest request, LabSession session) =>
{
    session.RenewLease(request.InstanceId, request.ClientId);
    return Results.NoContent();
});

app.Run();

public sealed record LeaseRequest(string InstanceId, string ClientId);
