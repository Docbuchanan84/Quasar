namespace Quasar.Simulation;

/// <summary>A full-thrust segment under central gravity with an inertial direction fixed at ignition.</summary>
internal sealed class PoweredTrajectory
{
    internal const int MaximumDurationSeconds = 3600;
    private const double StepSeconds = 1;
    private readonly CelestialState initial;
    private readonly Vector3d direction;
    private readonly double mu;
    private readonly double thrust;
    private readonly PropulsionPerformance performance;
    private readonly double poweredSeconds;

    public PoweredTrajectory(CelestialState initial, Velocity3 unitDirection, double mu,
        SpacecraftPropulsion propulsion, long startTicks, int durationSeconds)
        : this(initial, unitDirection, mu, propulsion, startTicks, (long)durationSeconds * TimeSpan.TicksPerSecond) { }

    internal PoweredTrajectory(CelestialState initial, Velocity3 unitDirection, double mu,
        SpacecraftPropulsion propulsion, long startTicks, long durationTicks)
    {
        initial.RequireFinite();
        direction = Vector3d.From(unitDirection);
        if (!double.IsFinite(direction.Length) || Math.Abs(direction.Length - 1) > 1e-9)
            throw new ArgumentException("Burn direction must be a finite unit vector.", nameof(unitDirection));
        if (!double.IsFinite(mu) || mu <= 0)
            throw new ArgumentOutOfRangeException(nameof(mu));
        if (durationTicks is < 1 or > MaximumDurationSeconds * TimeSpan.TicksPerSecond)
            throw new ArgumentOutOfRangeException(nameof(durationTicks));

        performance = propulsion.CalculatePerformance();
        if (performance.AvailablePropellantKg == 0)
            throw new InvalidOperationException("Cannot ignite with an empty propellant tank.");

        this.initial = initial;
        this.mu = mu;
        thrust = propulsion.Engine.ThrustN;
        StartTicks = startTicks;
        var durationSeconds = durationTicks / (double)TimeSpan.TicksPerSecond;
        var fuelSeconds = performance.AvailablePropellantKg / performance.RatedMassFlowKgPerSecond;
        ExhaustsPropellant = fuelSeconds <= durationSeconds;
        poweredSeconds = Math.Min(durationSeconds, fuelSeconds);
        // Round the event up to a simulation tick. Integrate thrust only through physical depletion.
        EndTicks = checked(StartTicks + (ExhaustsPropellant
            ? Math.Max(1L, (long)Math.Ceiling(poweredSeconds * TimeSpan.TicksPerSecond)) : durationTicks));
    }

    public long StartTicks { get; }
    public long EndTicks { get; }
    public bool ExhaustsPropellant { get; }

    public double RemainingPropellantKg(long atTicks)
    {
        var seconds = SecondsAt(atTicks);
        return ExhaustsPropellant && atTicks == EndTicks
            ? 0
            : Math.Max(0, performance.AvailablePropellantKg - seconds * performance.RatedMassFlowKgPerSecond);
    }

    public CelestialState StateAt(long atTicks) => Integrate(initial, 0, SecondsAt(atTicks));

    internal CelestialState InitialStateFor(CelestialState end) => Integrate(end, poweredSeconds, 0);

    internal void InspectPath(Action<long, Position3> inspect) =>
        Integrate(initial, 0, poweredSeconds, (time, position) => inspect(StartTicks + (long)Math.Round(time * TimeSpan.TicksPerSecond), position));

    private CelestialState Integrate(CelestialState state, double from, double to, Action<double, Position3>? inspect = null)
    {
        var r = Vector3d.From(state.PositionKm);
        var v = Vector3d.From(state.VelocityKmPerSecond);
        inspect?.Invoke(from, r.Position);
        var directionOfTime = Math.Sign(to - from);
        // Anchor every substep to ignition so queries and caller advance partitions agree.
        for (var time = from; time != to;)
        {
            var next = Math.Abs(to - time) <= StepSeconds ? to : time + directionOfTime * StepSeconds;
            var step = next - time;
            var half = step / 2;
            var a1 = Acceleration(r, time);
            var v2 = v + a1 * half;
            var a2 = Acceleration(r + v * half, time + half);
            var v3 = v + a2 * half;
            var a3 = Acceleration(r + v2 * half, time + half);
            var v4 = v + a3 * step;
            var a4 = Acceleration(r + v3 * step, time + step);
            r += (v + v2 * 2 + v3 * 2 + v4) * (step / 6);
            v += (a1 + a2 * 2 + a3 * 2 + a4) * (step / 6);
            time = next;
            inspect?.Invoke(time, r.Position);
        }

        var result = new CelestialState(r.Position, v.Velocity);
        result.RequireFinite();
        return result;
    }

    private double SecondsAt(long atTicks)
    {
        if (atTicks < StartTicks || atTicks > EndTicks)
            throw new ArgumentOutOfRangeException(nameof(atTicks));
        return Math.Min(poweredSeconds, (atTicks - StartTicks) / (double)TimeSpan.TicksPerSecond);
    }

    private Vector3d Acceleration(Vector3d position, double seconds)
    {
        var radius = position.Length;
        if (!double.IsFinite(radius) || radius <= 0)
            throw new InvalidOperationException("Powered trajectory reached an invalid gravity radius.");
        var propellant = Math.Max(0, performance.AvailablePropellantKg - performance.RatedMassFlowKgPerSecond * seconds);
        var mass = performance.DryMassKg + propellant;
        return position / radius * (-mu / radius / radius) + direction * (thrust / mass / 1000);
    }
}
