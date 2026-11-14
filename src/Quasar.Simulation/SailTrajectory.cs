namespace Quasar.Simulation;

/// <summary>One bounded trim in a primary-relative inertial frame. Knots are anchored to trim start.</summary>
internal sealed class SailTrajectory
{
    internal const int MaximumDurationSeconds = 30 * 86400;
    private readonly CelestialState[] knots;
    private readonly Func<Vector3d, Vector3d, double, Vector3d> acceleration;
    private readonly long stepTicks;
    internal ActiveSailState Order { get; }
    internal long EndTicks { get; }
    internal TwoBodyTrajectory EndCoast { get; }

    internal SailTrajectory(CelestialPositionService catalogue, string primary, SolarSail sail, ActiveSailState order,
        Func<Func<Func<long, Vector3d, Vector3d, double, double, bool>, bool>, bool> validatePath)
    {
        SolarSail.ValidateAngle(order.AngleDegrees);
        if (order.DurationSeconds is < 1 or > MaximumDurationSeconds)
            throw new ArgumentException("Choose a sail trim lasting up to 30 days.");

        Order = order;
        EndTicks = checked(order.StartTicks + (long)order.DurationSeconds * TimeSpan.TicksPerSecond);
        var mu = catalogue.GetEnvironment(primary).GravitationalParameterKm3PerSecond2;
        var solarPrimary = primary == SolarSystem.Sun;
        stepTicks = (solarPrimary ? 600L : 30L) * TimeSpan.TicksPerSecond;
        var minimumSolarRadius = Math.Max(1, catalogue.Bodies[SolarSystem.Sun].RadiusKm);
        var maximumSailAcceleration = sail.FaceOnAcceleration(minimumSolarRadius) / 1000;
        acceleration = Acceleration;
        var durationTicks = EndTicks - order.StartTicks;
        knots = new CelestialState[(int)((durationTicks + stepTicks - 1) / stepTicks) + 1];
        knots[0] = order.InitialRelativeState.ToRuntime();
        if (!validatePath(BuildKnots))
            throw new InvalidOperationException("This trim crosses a gravity boundary or passes too close to a surface. Shorten the trim or feather the sail and coast through.");
        EndCoast = new(knots[^1], EndTicks, mu);

        bool BuildKnots(Func<long, Vector3d, Vector3d, double, double, bool> clear)
        {
            for (var i = 0; i < knots.Length - 1; i++)
            {
                var from = order.StartTicks + i * stepTicks;
                var through = from + Math.Min(stepTicks, EndTicks - from);
                var position = Vector3d.From(knots[i].PositionKm);
                var velocity = Vector3d.From(knots[i].VelocityKmPerSecond);
                var step = Seconds(through - from);
                var halfRadius = position.Length / 2;
                var deviation = (mu / halfRadius / halfRadius + maximumSailAcceleration) * step * step / 2;
                if (!(velocity.Length * step + deviation < halfRadius) || !clear(from, position, velocity, step, deviation))
                    return false;
                knots[i + 1] = Step(knots[i], from, through);
            }
            return clear(EndTicks, Vector3d.From(knots[^1].PositionKm), default, 0, 0);
        }

        Vector3d Acceleration(Vector3d position, Vector3d velocity, double elapsedSeconds)
        {
            var at = order.StartTicks + (long)Math.Round(elapsedSeconds * TimeSpan.TicksPerSecond);
            if (!solarPrimary)
            {
                var primaryState = catalogue.GetState(primary, at);
                var sun = catalogue.GetState(SolarSystem.Sun, at);
                var offset = TrajectoryPlanner.Subtract(primaryState, sun);
                velocity += Vector3d.From(offset.VelocityKmPerSecond);
                var sunToShip = position + Vector3d.From(offset.PositionKm);
                return Gravity(position) + Sunlight(sunToShip, velocity);
            }
            return Gravity(position) + Sunlight(position, velocity);
        }

        Vector3d Gravity(Vector3d position)
        {
            var radius = position.Length;
            return position / radius * (-mu / radius / radius);
        }

        Vector3d Sunlight(Vector3d sunToShip, Vector3d velocity)
        {
            if (!(sunToShip.Length >= minimumSolarRadius))
                throw new InvalidOperationException("Sail trim reaches the solar surface.");
            return sail.Acceleration(sunToShip, velocity, order.AngleDegrees);
        }
    }

    internal CelestialState StateAt(long atTicks)
    {
        if (atTicks < Order.StartTicks || atTicks > EndTicks)
            throw new ArgumentOutOfRangeException(nameof(atTicks));
        if (atTicks == EndTicks) return knots[^1];
        var index = (int)((atTicks - Order.StartTicks) / stepTicks);
        return Step(knots[index], Order.StartTicks + index * stepTicks, atTicks);
    }

    private CelestialState Step(CelestialState state, long from, long through)
    {
        if (from == through) return state;
        var position = Vector3d.From(state.PositionKm);
        var velocity = Vector3d.From(state.VelocityKmPerSecond);
        RungeKutta.Step(ref position, ref velocity, Seconds(from - Order.StartTicks), Seconds(through - from), acceleration);
        var result = new CelestialState(position.Position, velocity.Velocity);
        result.RequireFinite();
        return result;
    }

    private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;
}
