using Quasar.Simulation;

namespace Quasar.Server;

internal sealed record TorchFitStatus(string InstanceId, long Revision, string? Reason);

internal static class TorchDesignerEndpoints
{
    public static void MapTorchDesigner(this WebApplication app)
    {
        var routes = app.MapGroup("/api/torch-designer");
        routes.MapGet("/preview", (double jetPowerGw, double exhaustSpeedKmPerSecond, double nozzleDiameterM) =>
            Evaluate(() => FusionTorchDesigner.Preview(new(jetPowerGw, exhaustSpeedKmPerSecond, nozzleDiameterM))));
        routes.MapGet("/test-burn", (double jetPowerGw, double exhaustSpeedKmPerSecond, double nozzleDiameterM) =>
            Evaluate(() => FusionTorchDesigner.CompareBurn(new(jetPowerGw, exhaustSpeedKmPerSecond, nozzleDiameterM))));
        routes.MapGet("/fit-status", (LabSession session) => session.GetTorchFitStatus());
    }

    private static IResult Evaluate<T>(Func<T> evaluate)
    {
        try { return Results.Ok(evaluate()); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    }
}
