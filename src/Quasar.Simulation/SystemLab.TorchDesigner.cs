namespace Quasar.Simulation;

public sealed partial class SystemLab
{
    public string? TorchFitBlockReason()
    {
        var ship = movement.View(ShipId);
        if (ship.BurnEndTicks is not null) return "Cut thrust in the System Lab before fitting a drive.";
        if (movement.CommittedPlan(ShipId) is not null) return "Finish or cancel the course before fitting a drive.";
        if (movement.PropulsionOf(ShipId)?.Tank.PropellantId != FusionTorchDesigner.ReactionMassId)
            return "Pathfinder needs the stock torch’s reaction-mass tank. Fit the stock torch in the System Lab first.";
        return ship.Status == "Coasting" ? null : "Pathfinder must be freely coasting to fit a drive.";
    }

    public void FitDesignedTorch(FusionTorchDesign design)
    {
        if (TorchFitBlockReason() is { } reason) throw new InvalidOperationException(reason);
        var engine = FusionTorchDesigner.BuildEngine(design, out _);
        var propulsion = movement.PropulsionOf(ShipId)! with { Engine = engine };
        movement.RefitCoastingSpacecraft(ShipId, propulsion);
        Record("refit", FormattableString.Invariant(
            $"Pathfinder fitted a fusion torch rated at {engine.ThrustN / 1000:0.##} kN and {design.ExhaustSpeedKmPerSecond:0.##} km/s."));
    }

    internal static TorchBurnResult TestTorchBurn(SpacecraftPropulsion propulsion, int durationSeconds)
    {
        var testShip = new SystemLab(propulsion);
        var end = TimeSpan.FromSeconds(durationSeconds);
        var coast = testShip.ShipStateAt(end.Ticks);
        testShip.StartBurn("prograde", durationSeconds);
        testShip.Advance(end);
        var burned = testShip.ShipStateAt(end.Ticks);
        var remaining = testShip.movement.PropulsionOf(ShipId)!.Tank.QuantityKg;
        var velocityChange = Vector3d.From(burned.VelocityKmPerSecond) - Vector3d.From(coast.VelocityKmPerSecond);
        return new(propulsion.Tank.QuantityKg - remaining, velocityChange.Length);
    }
}
