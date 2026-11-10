namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    private sealed record ActiveBurn(ActiveBurnState State, PoweredTrajectory Path, TwoBodyTrajectory EndCoast);

    public bool IsBurning(string id) => spacecraft[id].Burn is not null;

    public void RefitCoastingSpacecraft(string id, SpacecraftPropulsion propulsion)
    {
        ArgumentNullException.ThrowIfNull(propulsion);
        propulsion.CalculatePerformance();
        var ship = spacecraft[id];
        if (ship.Coast is null) throw new InvalidOperationException("Cancel the course or cut thrust before refitting.");
        var coast = NewCoast(StateAt(id, ElapsedTicks), ship.Primary);
        ship.Coast = coast;
        ship.Propulsion = propulsion;
        ship.Generation++;
    }

    public void StartBurn(string id, string direction, int durationSeconds)
    {
        var sign = direction switch
        {
            "prograde" => 1,
            "retrograde" => -1,
            _ => throw new ArgumentException("Choose prograde or retrograde.", nameof(direction))
        };
        var ship = spacecraft[id];
        if (ship.Coast is not { } coast)
            throw new InvalidOperationException("The ship must be coasting freely before ignition.");
        var propulsion = ship.Propulsion ?? throw new InvalidOperationException("Propulsion is not modeled for this ship.");
        var initial = coast.StateAt(ElapsedTicks);
        var velocity = Vector3d.From(initial.VelocityKmPerSecond);
        if (!double.IsFinite(velocity.Length) || velocity.Length <= 0)
            throw new InvalidOperationException("The ship has no usable velocity relative to its gravity primary.");

        var saved = new ActiveBurnState(ElapsedTicks, durationSeconds, KinematicState.From(initial),
            CartesianState.From((velocity / velocity.Length * sign).Velocity), propulsion.Tank.QuantityKg);
        // Validate the bounded powered segment and its handoff before changing the ship or queue.
        var burn = BuildBurn(ship.Primary, propulsion, saved);
        ship.Burn = burn;
        ship.Coast = null;
        ship.AutomaticPrimary = false;
        ship.Generation++;
        Schedule(id, ship);
        CompactQueue();
    }

    public void CutThrust(string id)
    {
        var ship = spacecraft[id];
        var burn = ship.Burn ?? throw new InvalidOperationException("No active burn to cut off.");
        var coast = new TwoBodyTrajectory(burn.Path.StateAt(ElapsedTicks), ElapsedTicks, PrimaryMu(ship.Primary));
        EndBurn(ship, ElapsedTicks, coast);
        CompactQueue();
    }

    private ActiveBurn BuildBurn(string primary, SpacecraftPropulsion propulsion, ActiveBurnState state)
    {
        var mu = PrimaryMu(primary);
        var path = new PoweredTrajectory(state.InitialRelativeState.ToRuntime(), state.UnitDirection.ToVelocity(),
            mu, propulsion, state.StartTicks, state.DurationSeconds);
        var endCoast = new TwoBodyTrajectory(path.StateAt(path.EndTicks), path.EndTicks, mu);
        return new(state, path, endCoast);
    }

    private static SpacecraftPropulsion WithPropellant(SpacecraftPropulsion propulsion, double quantityKg) =>
        FiniteCourse.WithPropellant(propulsion, quantityKg);

    internal SpacecraftPropulsion? PropulsionOf(string id) => PropulsionAt(spacecraft[id], ElapsedTicks);

    private static SpacecraftPropulsion? PropulsionAt(Spacecraft ship, long atTicks) => ship.Burn is { } burn
        ? WithPropellant(ship.Propulsion!, burn.Path.RemainingPropellantKg(atTicks))
        : ship.Plan?.Finite is { } finite ? WithPropellant(finite.State.Propulsion, finite.PropellantAt(atTicks)) : ship.Propulsion;

    private static void EndBurn(Spacecraft ship, long atTicks, TwoBodyTrajectory coast)
    {
        ship.Propulsion = PropulsionAt(ship, atTicks);
        ship.Burn = null;
        ship.Coast = coast;
        ship.AutomaticPrimary = true;
        ship.Generation++;
    }
}
