namespace Quasar.Simulation;

public sealed record CourseArrival(string Kind, string TargetId, string PrimaryBodyId, double TargetRadiusKm,
    double PositionErrorKm, double VelocityErrorKmPerSecond);

internal sealed record FiniteCourse(FiniteCourseState State, PoweredTrajectory Departure, PoweredTrajectory Braking,
    TrajectorySegment Coast, TwoBodyTrajectory ArrivalCoast, double BrakingPropellantKg, double RemainingPropellantKg,
    CourseArrival Arrival)
{
    internal const double PositionToleranceKm = 0.01;
    internal const double VelocityToleranceKmPerSecond = 1e-6;

    internal double PropellantAt(long ticks) => ticks <= Departure.EndTicks
        ? Departure.RemainingPropellantKg(ticks)
        : Braking.RemainingPropellantKg(Math.Min(Braking.EndTicks, Math.Max(ticks, Braking.StartTicks)));

    internal CelestialState StateAt(long ticks) => ticks == Departure.StartTicks ? State.InitialState.ToRuntime()
        : ticks <= Departure.EndTicks
        ? OrbitalNavigation.ToSystem(Coast.Catalogue, Coast.PrimaryBodyId, ticks, Departure.StateAt(ticks))
        : ticks < Braking.StartTicks ? Coast.StateAt(ticks)
        : OrbitalNavigation.ToSystem(Coast.Catalogue, Coast.PrimaryBodyId, ticks, Braking.StateAt(ticks));

    internal static SpacecraftPropulsion WithPropellant(SpacecraftPropulsion propulsion, double quantityKg) =>
        propulsion with { Tank = propulsion.Tank with { QuantityKg = quantityKg } };

    internal static FiniteCourse Build(CelestialPositionService catalogue, FiniteCourseState state)
    {
        if (state.InitialState is null || state.TargetLocalState is null || state.Propulsion is null
            || state.DepartureDirection is null || state.BrakingDirection is null
            || state.ArrivalKind is not ("approach" or "orbit") || !catalogue.Bodies.ContainsKey(state.TargetId)
            || state.ArrivalKind == "orbit" && state.TargetId != state.PrimaryBodyId
            || state.ArrivalKind == "approach" && catalogue.Bodies[state.TargetId].ParentId != state.PrimaryBodyId
            || (decimal)state.ArrivalTicks - state.DepartureTicks > TimeSpan.TicksPerSecond * (decimal)CourseSearch.MaximumSeconds)
            throw new ArgumentException("Incomplete or unsupported finite course.");

        var primary = catalogue.Bodies[state.PrimaryBodyId];
        var mu = primary.GravitationalParameterKm3PerSecond2;
        var clearance = Math.Max(1, primary.RadiusKm);
        var local = OrbitalNavigation.ToLocal(catalogue, primary.Id, state.DepartureTicks, state.InitialState.ToRuntime());
        var departure = new PoweredTrajectory(local, state.DepartureDirection.ToVelocity(),
            mu, state.Propulsion, state.DepartureTicks, state.DepartureDurationTicks);
        var brakingStart = checked(state.ArrivalTicks - state.BrakingDurationTicks);
        if (departure.ExhaustsPropellant || departure.EndTicks >= brakingStart)
            throw new ArgumentException("Departure burn leaves insufficient fuel or coast time for braking.");

        var departureEnd = departure.StateAt(departure.EndTicks);
        var coast = new TwoBodyTrajectory(departureEnd, departure.EndTicks, mu);
        if (coast.PeriapsisKm <= clearance)
            throw new ArgumentException("Finite course violates primary clearance.");

        var brakingPropulsion = WithPropellant(state.Propulsion, departure.RemainingPropellantKg(departure.EndTicks));
        var braking = new PoweredTrajectory(coast.StateAt(brakingStart), state.BrakingDirection.ToVelocity(),
            mu, brakingPropulsion, brakingStart, state.BrakingDurationTicks);
        if (braking.ExhaustsPropellant)
            throw new ArgumentException("Insufficient propellant to complete braking with a reserve.");

        var target = state.TargetLocalState.ToRuntime();
        var achieved = braking.StateAt(state.ArrivalTicks);
        var positionError = target.PositionKm.DistanceTo(achieved.PositionKm);
        var velocityError = (Vector3d.From(target.VelocityKmPerSecond) - Vector3d.From(achieved.VelocityKmPerSecond)).Length;
        if (positionError > PositionToleranceKm || velocityError > VelocityToleranceKmPerSecond)
            throw new ArgumentException("Finite burns did not converge on the requested arrival state.");

        var end = new TwoBodyTrajectory(achieved, state.ArrivalTicks, mu);
        if (state.ArrivalKind == "orbit")
            OrbitalNavigation.ResolveOrbit(catalogue, primary.Id, state.ArrivalTicks, achieved, state.ArrivalTicks);
        var targetLocal = OrbitalNavigation.ToLocal(catalogue, state.TargetId, state.ArrivalTicks,
            OrbitalNavigation.ToSystem(catalogue, primary.Id, state.ArrivalTicks, target));
        var reserve = braking.RemainingPropellantKg(braking.EndTicks);
        return new(state, departure, braking, new(catalogue, primary.Id, coast, brakingStart), end,
            brakingPropulsion.Tank.QuantityKg - reserve, reserve,
            new(state.ArrivalKind, state.TargetId, primary.Id, Vector3d.From(targetLocal.PositionKm).Length, positionError, velocityError));
    }

    internal TrajectoryPlan ToPlan()
    {
        var catalogue = Coast.Catalogue;
        var exhaustSpeed = State.Propulsion.Engine.ExhaustVelocityMPerSecond / 1000;
        var mass = State.Propulsion.CalculatePerformance();
        var brakingWetMass = mass.DryMassKg + Braking.RemainingPropellantKg(Braking.StartTicks);
        var arrivalWetMass = mass.DryMassKg + RemainingPropellantKg;
        var target = OrbitalNavigation.ToSystem(catalogue, State.PrimaryBodyId, State.ArrivalTicks, State.TargetLocalState.ToRuntime());
        return new(catalogue, State.InitialState.ToRuntime(), new(null, target, State.ArrivalTicks), [Coast],
            [new(Departure.StartTicks, "departure", (Vector3d.From(State.DepartureDirection.ToVelocity())
                * (exhaustSpeed * Math.Log(mass.WetMassKg / brakingWetMass))).Velocity),
             new(Braking.StartTicks, "braking", (Vector3d.From(State.BrakingDirection.ToVelocity())
                * (exhaustSpeed * Math.Log(brakingWetMass / arrivalWetMass))).Velocity)],
            new(mass.AvailablePropellantKg - RemainingPropellantKg, null), finite: this);
    }
}
