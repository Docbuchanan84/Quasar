namespace Quasar.Simulation;

/// <summary>A full-thrust segment under central gravity with an inertial direction fixed at ignition.</summary>
internal sealed class PoweredTrajectory
{
    internal const int MaximumDurationSeconds = 3600;
    internal const int MaximumCourseDurationSeconds = 86400;
    private const double StepSeconds = 1;
    private readonly CelestialState initial;
    private readonly Vector3d direction;
    private readonly double mu;
    private readonly double thrust;
    private readonly PropulsionPerformance performance;
    private readonly double poweredSeconds;

    public PoweredTrajectory(CelestialState initial, Velocity3 unitDirection, double mu,
        SpacecraftPropulsion propulsion, long startTicks, int durationSeconds)
        : this(initial, unitDirection, mu, propulsion, startTicks, (long)durationSeconds * TimeSpan.TicksPerSecond)
    {
        if (durationSeconds > MaximumDurationSeconds) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
    }

    internal PoweredTrajectory(CelestialState initial, Velocity3 unitDirection, double mu,
        SpacecraftPropulsion propulsion, long startTicks, long durationTicks)
    {
        initial.RequireFinite();
        direction = Vector3d.From(unitDirection);
        if (!double.IsFinite(direction.Length) || Math.Abs(direction.Length - 1) > 1e-9)
            throw new ArgumentException("Burn direction must be a finite unit vector.", nameof(unitDirection));
        if (!double.IsFinite(mu) || mu <= 0)
            throw new ArgumentOutOfRangeException(nameof(mu));
        if (durationTicks is < 1 or > MaximumCourseDurationSeconds * TimeSpan.TicksPerSecond)
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

    public CelestialState StateAt(long atTicks) => Integrate(initial, 0, SecondsAt(atTicks)).State;

    internal CelestialState InitialStateFor(CelestialState end) => Integrate(end, poweredSeconds, 0).State;

    internal bool HasClearPath(Func<long, Vector3d, Vector3d, double, double, bool> clear)
    {
        var maxThrustAcceleration = thrust / performance.DryMassKg / 1000 * direction.Length;
        var (end, stepsClear) = Integrate(initial, 0, poweredSeconds, InspectStep);
        return stepsClear && clear(EndTicks, Vector3d.From(end.PositionKm), default, 0, 0);

        bool InspectStep(double time, Vector3d position, Vector3d velocity, double step)
        {
            // Before the first possible exit below half the initial radius, gravity is bounded
            // by GM/r_min² and thrust by its dry-mass limit. If the displacement bound cannot
            // reach r_min, those bounds hold throughout this step. The RK4 stage positions
            // and any partial RK4 step also stay inside the same tangent envelope.
            var minimumRadius = position.Length / 2;
            var maxAcceleration = mu / minimumRadius / minimumRadius + maxThrustAcceleration;
            var deviation = maxAcceleration * step * step / 2;
            return velocity.Length * step + deviation < minimumRadius
                && clear(StartTicks + (long)Math.Round(time * TimeSpan.TicksPerSecond), position, velocity, step, deviation);
        }
    }

    private (CelestialState State, bool Clear) Integrate(CelestialState state, double from, double to,
        Func<double, Vector3d, Vector3d, double, bool>? inspect = null)
    {
        var r = Vector3d.From(state.PositionKm);
        var v = Vector3d.From(state.VelocityKmPerSecond);
        var directionOfTime = Math.Sign(to - from);
        // Anchor every substep to ignition so queries and caller advance partitions agree.
        for (var time = from; time != to;)
        {
            var next = Math.Abs(to - time) <= StepSeconds ? to : time + directionOfTime * StepSeconds;
            var step = next - time;
            if (inspect is not null && !inspect(time, r, v, step)) return (default, false);
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
        }

        var result = new CelestialState(r.Position, v.Velocity);
        result.RequireFinite();
        return (result, true);
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
