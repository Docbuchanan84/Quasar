namespace Quasar.Simulation;

public sealed record CourseArrival(string Kind, string TargetId, string PrimaryBodyId, double TargetRadiusKm,
    double PositionErrorKm, double VelocityErrorKmPerSecond);

internal sealed record FiniteCourse(FiniteCourseState State, CourseBurn Departure, CourseBurn Braking,
    TrajectorySegment Coast, TwoBodyTrajectory ArrivalCoast, double BrakingPropellantKg, double RemainingPropellantKg,
    CourseArrival Arrival, TrajectorySegment[] Coasts)
{
    internal const double PositionToleranceKm = 0.01;
    internal const double VelocityToleranceKmPerSecond = 1e-6;

    internal double PropellantAt(long ticks) => ticks <= Departure.EndTicks
        ? Departure.RemainingPropellantKg(ticks)
        : Braking.RemainingPropellantKg(Math.Min(Braking.EndTicks, Math.Max(ticks, Braking.StartTicks)));

    internal CelestialState StateAt(long ticks) => ticks == Departure.StartTicks ? State.InitialState.ToRuntime()
        : ticks <= Departure.EndTicks
        ? Departure.StateAt(ticks)
        : ticks < Braking.StartTicks ? Coasts.Last(coast => coast.StartTicks <= ticks).StateAt(ticks)
        : Braking.StateAt(ticks);

    internal string PrimaryAt(long ticks) => ticks < Departure.EndTicks ? Departure.PrimaryAt(ticks)
        : ticks < Braking.StartTicks ? Coasts.Last(coast => coast.StartTicks <= ticks).PrimaryBodyId : Braking.PrimaryAt(ticks);

    internal long[] GravityTransitionTicks => Departure.Parts.Select(part => part.Path.StartTicks)
        .Concat(Coasts.Select(coast => coast.StartTicks)).Concat(Braking.Parts.Select(part => part.Path.StartTicks))
        .Distinct().Where(ticks => ticks > Departure.StartTicks && PrimaryAt(ticks) != PrimaryAt(ticks - 1)).Order().ToArray();

    internal (long Ticks, string Kind)[] Boundaries => GravityTransitionTicks
        .Where(ticks => ticks != Departure.EndTicks && ticks != Braking.StartTicks).Select(ticks => (ticks, "primary-change"))
        .Concat([(Departure.EndTicks, "course-departure-cutoff"), (Braking.StartTicks, "course-braking-start")])
        .Append((Braking.EndTicks, Arrival.Kind == "orbit" ? "course-orbit-inserted" : "course-approach-complete"))
        .OrderBy(boundary => boundary.Item1).ToArray();

    internal static SpacecraftPropulsion WithPropellant(SpacecraftPropulsion propulsion, double quantityKg) =>
        propulsion with { Tank = propulsion.Tank with { QuantityKg = quantityKg } };

    internal static SpacecraftPropulsion Throttled(SpacecraftPropulsion propulsion, double throttle)
    {
        if (!double.IsFinite(throttle) || throttle is <= 0 or > 1) throw new ArgumentException("Invalid finite burn throttle.");
        return propulsion with { Engine = propulsion.Engine with { ThrustN = propulsion.Engine.ThrustN * throttle } };
    }

    internal static FiniteCourse Build(CelestialPositionService catalogue, FiniteCourseState state,
        CancellationToken cancellationToken = default)
    {
        var arrivalPrimary = state.ArrivalPrimaryBodyId ?? state.PrimaryBodyId;
        if (state.ArrivalPrimaryBodyId is null && (state.DepartureThrottle != 1 || state.BrakingThrottle != 1))
            throw new ArgumentException("Legacy finite courses require full thrust.");
        if (state.InitialState is null || state.TargetLocalState is null || state.Propulsion is null
            || state.DepartureDirection is null || state.BrakingDirection is null
            || state.ArrivalKind is not ("approach" or "orbit") || !catalogue.Bodies.ContainsKey(state.TargetId)
            || state.ArrivalKind == "orbit" && state.TargetId != arrivalPrimary
            || state.ArrivalKind == "approach" && catalogue.Bodies[state.TargetId].ParentId != arrivalPrimary && state.TargetId != arrivalPrimary
            || (decimal)state.ArrivalTicks - state.DepartureTicks > TimeSpan.TicksPerSecond * (decimal)CourseSearch.MaximumSeconds)
            throw new ArgumentException("Incomplete or unsupported finite course.");

        var primary = catalogue.Bodies[state.PrimaryBodyId];
        var mu = primary.GravitationalParameterKm3PerSecond2;
        var clearance = Math.Max(1, primary.RadiusKm);
        var local = OrbitalNavigation.ToLocal(catalogue, primary.Id, state.DepartureTicks, state.InitialState.ToRuntime());
        var departure = state.ArrivalPrimaryBodyId is not null
            ? CourseBurn.Build(catalogue, state.InitialState.ToRuntime(), primary.Id, state.DepartureTicks,
                state.DepartureDurationTicks, state.DepartureDirection.ToVelocity(), Throttled(state.Propulsion, state.DepartureThrottle), cancellationToken)
            : CourseBurn.Single(catalogue, state.Propulsion, primary.Id, new(local, state.DepartureDirection.ToVelocity(),
                mu, state.Propulsion, state.DepartureTicks, state.DepartureDurationTicks));
        var brakingStart = checked(state.ArrivalTicks - state.BrakingDurationTicks);
        if (departure.ExhaustsPropellant || departure.EndTicks >= brakingStart)
            throw new ArgumentException("Departure burn leaves insufficient fuel or coast time for braking.");

        var departureEnd = departure.StateAt(departure.EndTicks);
        var coast = new TwoBodyTrajectory(OrbitalNavigation.ToLocal(catalogue, primary.Id, departure.EndTicks, departureEnd), departure.EndTicks, mu);
        if (state.ArrivalPrimaryBodyId is null && coast.PeriapsisKm <= clearance)
            throw new ArgumentException("Finite course violates primary clearance.");

        var coasts = state.ArrivalPrimaryBodyId is not null
            ? new MovementExecution(catalogue).TraceCourseCoast(departureEnd, departure.PrimaryAt(departure.EndTicks),
                departure.EndTicks, brakingStart, cancellationToken)
            : [new TrajectorySegment(catalogue, primary.Id, coast, brakingStart)];

        var brakingPropulsion = WithPropellant(state.Propulsion, departure.RemainingPropellantKg(departure.EndTicks));
        var last = coasts[^1];
        var braking = state.ArrivalPrimaryBodyId is not null
            ? CourseBurn.Build(catalogue, last.StateAt(brakingStart), last.PrimaryBodyId, brakingStart,
                state.BrakingDurationTicks, state.BrakingDirection.ToVelocity(), Throttled(brakingPropulsion, state.BrakingThrottle), cancellationToken)
            : CourseBurn.Single(catalogue, brakingPropulsion, primary.Id,
                new(last.Coast.StateAt(brakingStart), state.BrakingDirection.ToVelocity(), mu,
                    brakingPropulsion, brakingStart, state.BrakingDurationTicks));
        if (braking.ExhaustsPropellant)
            throw new ArgumentException("Insufficient propellant to complete braking with a reserve.");

        var target = state.TargetLocalState.ToRuntime();
        if (braking.PrimaryAt(state.ArrivalTicks) != arrivalPrimary)
            throw new ArgumentException("Finite course did not reach the destination gravity system.");
        var achieved = OrbitalNavigation.ToLocal(catalogue, arrivalPrimary, state.ArrivalTicks, braking.StateAt(state.ArrivalTicks));
        var positionError = target.PositionKm.DistanceTo(achieved.PositionKm);
        var velocityError = (Vector3d.From(target.VelocityKmPerSecond) - Vector3d.From(achieved.VelocityKmPerSecond)).Length;
        if (positionError > PositionToleranceKm || velocityError > VelocityToleranceKmPerSecond)
            throw new ArgumentException("Finite burns did not converge on the requested arrival state.");

        var end = new TwoBodyTrajectory(achieved, state.ArrivalTicks, catalogue.Bodies[arrivalPrimary].GravitationalParameterKm3PerSecond2);
        if (state.ArrivalKind == "orbit")
            OrbitalNavigation.ResolveOrbit(catalogue, arrivalPrimary, state.ArrivalTicks, achieved, state.ArrivalTicks);
        var targetLocal = OrbitalNavigation.ToLocal(catalogue, state.TargetId, state.ArrivalTicks,
            OrbitalNavigation.ToSystem(catalogue, arrivalPrimary, state.ArrivalTicks, target));
        var reserve = braking.RemainingPropellantKg(braking.EndTicks);
        return new(state, departure, braking, coasts[0], end,
            brakingPropulsion.Tank.QuantityKg - reserve, reserve,
            new(state.ArrivalKind, state.TargetId, arrivalPrimary, Vector3d.From(targetLocal.PositionKm).Length, positionError, velocityError), coasts);
    }

    internal TrajectoryPlan ToPlan()
    {
        var catalogue = Coast.Catalogue;
        var exhaustSpeed = State.Propulsion.Engine.ExhaustVelocityMPerSecond / 1000;
        var mass = State.Propulsion.CalculatePerformance();
        var brakingWetMass = mass.DryMassKg + Braking.RemainingPropellantKg(Braking.StartTicks);
        var arrivalWetMass = mass.DryMassKg + RemainingPropellantKg;
        var target = OrbitalNavigation.ToSystem(catalogue, Arrival.PrimaryBodyId, State.ArrivalTicks, State.TargetLocalState.ToRuntime());
        return new(catalogue, State.InitialState.ToRuntime(), new(null, target, State.ArrivalTicks), Coasts,
            [new(Departure.StartTicks, "departure", (Vector3d.From(State.DepartureDirection.ToVelocity())
                * (exhaustSpeed * Math.Log(mass.WetMassKg / brakingWetMass))).Velocity),
             new(Braking.StartTicks, "braking", (Vector3d.From(State.BrakingDirection.ToVelocity())
                * (exhaustSpeed * Math.Log(brakingWetMass / arrivalWetMass))).Velocity)],
            new(mass.AvailablePropellantKg - RemainingPropellantKg, null), finite: this);
    }
}
