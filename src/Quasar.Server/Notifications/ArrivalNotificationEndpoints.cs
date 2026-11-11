using System.Security.Cryptography;
using System.Text.Json;

namespace Quasar.Server.Notifications;

internal static class ArrivalNotificationEndpoints
{
    private sealed record DeviceRequest(string Endpoint);

    public static void MapArrivalNotificationEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/notifications");
        routes.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
            {
                app.Logger.LogWarning("Arrival notification storage is unavailable ({ErrorType}).", error.GetType().Name);
                return Results.Json(new { error = "Arrival notifications are unavailable. Try again later." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        routes.MapGet("/key", (ArrivalSubscriptionStore store) => Results.Ok(new { publicKey = store.PublicKey() }));
        routes.MapPost("/status", (DeviceRequest request, ArrivalSubscriptionStore store) =>
            ArrivalSubscription.IsAppleEndpoint(request.Endpoint)
                ? Results.Ok(new { enabled = store.Read()?.Devices.Any(device => device.Endpoint == request.Endpoint) == true })
                : Unsupported());
        routes.MapPost("/subscribe", (ArrivalSubscription device, ArrivalSubscriptionStore store) =>
            !device.IsValid() ? Unsupported()
                : store.Register(device) ? Results.NoContent()
                : Results.Conflict(new { error = "The arrival notification device limit has been reached." }));
        routes.MapPost("/unsubscribe", (DeviceRequest request, ArrivalSubscriptionStore store) =>
        {
            if (!ArrivalSubscription.IsAppleEndpoint(request.Endpoint)) return Unsupported();
            store.Remove(request.Endpoint);
            return Results.NoContent();
        });
    }

    private static IResult Unsupported() =>
        Results.BadRequest(new { error = "Open the Quasar Home Screen web app on your iPhone to enable arrival notifications." });
}
