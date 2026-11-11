namespace Quasar.Simulation;

public sealed partial class TrajectoryPlanner
{
    private sealed record PatchedRequest(CelestialState Initial, long Start, long End, string Primary, string ArrivalPrimary,
        string Target, string Kind, CelestialState TargetState, SpacecraftPropulsion Propulsion, bool LongWay);
    private readonly Dictionary<PatchedRequest, (TrajectoryPlan? Plan, string Reason)> patchedCache = [];

    internal TrajectoryPlan? RefinePatchedCourse(CelestialState initial, long departureTicks, long arrivalTicks,
        string primaryId, string arrivalPrimary, string targetId, string arrivalKind, CelestialState target,
        SpacecraftPropulsion propulsion, double? budget, bool longWay, CancellationToken token, out string reason)
    {
        token.ThrowIfCancellationRequested();
        var key = new PatchedRequest(initial, departureTicks, arrivalTicks, primaryId, arrivalPrimary, targetId, arrivalKind, target, propulsion, longWay);
        (TrajectoryPlan? Plan, string Reason) result;
        bool cached;
        lock (gate) cached = patchedCache.TryGetValue(key, out result);
        if (!cached)
        {
            var plan = RefinePatchedCore(initial, departureTicks, arrivalTicks, primaryId, arrivalPrimary, targetId,
                arrivalKind, target, propulsion, longWay, token, out var failure);
            result = (plan, failure);
            lock (gate)
            {
                if (patchedCache.Count >= 256) patchedCache.Remove(patchedCache.Keys.First());
                patchedCache[key] = result;
            }
        }
        reason = result.Reason;
        if (result.Plan?.RequiredDeltaVKmPerSecond > budget)
        {
            reason = "The verified route needs more propellant than the selected reserve allows.";
            return null;
        }
        return result.Plan;
    }

    private TrajectoryPlan? RefinePatchedCore(CelestialState initial, long departureTicks, long arrivalTicks,
        string primaryId, string arrivalPrimary, string targetId, string arrivalKind, CelestialState target,
        SpacecraftPropulsion propulsion, bool longWay, CancellationToken token, out string reason)
    {
        reason = "No verified finite-burn route was found for this flight time and geometry.";
        var performance = propulsion.CalculatePerformance();
        var exhaust = propulsion.Engine.ExhaustVelocityMPerSecond / 1000;
        var tracer = new MovementExecution(catalogue);
        try
        {
            var ancestors = new HashSet<string>(StringComparer.Ordinal);
            for (string? ancestor = primaryId; ancestor is not null; ancestor = catalogue.Bodies[ancestor].ParentId) ancestors.Add(ancestor);
            var seedPrimary = arrivalPrimary;
            while (!ancestors.Contains(seedPrimary)) seedPrimary = catalogue.Bodies[seedPrimary].ParentId
                ?? throw new ArgumentException("Destination is outside the modeled celestial hierarchy.");
            var seed = Plan(new(initial, departureTicks, new(seedPrimary, arrivalTicks,
                new(null, target, arrivalTicks), longWay, 1)));
            var departure = Vector3d.From(seed.Burns[0].DeltaVelocityKmPerSecond);
            var braking = Vector3d.From(seed.Burns[^1].DeltaVelocityKmPerSecond);
            var localInitial = OrbitalNavigation.ToLocal(catalogue, primaryId, departureTicks, initial);
            var orbit = new TwoBodyTrajectory(localInitial, departureTicks, catalogue.Bodies[primaryId].GravitationalParameterKm3PerSecond2);
            var ignitionSpeed = Vector3d.From(localInitial.VelocityKmPerSecond).Length;
            var ignitionRadius = Vector3d.From(localInitial.PositionKm).Length;
            var estimatedBurn = -performance.WetMassKg * double.ExpM1(-departure.Length / exhaust) / performance.RatedMassFlowKgPerSecond;
            if (orbit.PeriodSeconds is { } period && estimatedBurn * ignitionSpeed / ignitionRadius > 0.05)
            {
                // In a tight orbit a finite burn spans a material angle. Seed its direction
                // at the burn midpoint rather than treating it as an impulse at ignition.
                var shift = (long)(Math.Min(period / 4, estimatedBurn / 2) * TimeSpan.TicksPerSecond);
                var mid = checked(departureTicks + shift);
                if (mid < arrivalTicks)
                {
                    var middle = OrbitalNavigation.ToSystem(catalogue, primaryId, mid, orbit.StateAt(mid));
                    var centered = Plan(new(middle, mid, new(seedPrimary, arrivalTicks, new(null, target, arrivalTicks), longWay, 1)));
                    departure = Vector3d.From(centered.Burns[0].DeltaVelocityKmPerSecond);
                    braking = Vector3d.From(centered.Burns[^1].DeltaVelocityKmPerSecond);
                }
            }
            var arrivalRadius = Vector3d.From(OrbitalNavigation.ToLocal(catalogue, arrivalPrimary, arrivalTicks, target).PositionKm).Length;
            if ((estimatedBurn * ignitionSpeed / ignitionRadius > 0.05
                && catalogue.Bodies[primaryId].GravitationalParameterKm3PerSecond2 / ignitionRadius / ignitionRadius
                    > performance.InitialAccelerationMPerSecond2 / 2000)
                || catalogue.Bodies[arrivalPrimary].GravitationalParameterKm3PerSecond2 / arrivalRadius / arrivalRadius
                    > performance.InitialAccelerationMPerSecond2 / 10000)
            {
                var shot = ShootPatchedCourse(initial, departureTicks, arrivalTicks, primaryId, arrivalPrimary, targetId,
                    arrivalKind, target, propulsion, departure, braking, token);
                if (shot is not null) return shot;
            }
            for (var iteration = 0; iteration < 18; iteration++)
            {
                token.ThrowIfCancellationRequested();
                var total = departure.Length + braking.Length;
                if (!double.IsFinite(total) || total >= performance.TheoreticalDeltaVMPerSecond / 1000)
                {
                    reason = "Insufficient propellant for departure, gravity transitions, braking and the selected reserve.";
                    return null;
                }
                var (firstTicks, firstThrottle) = BurnTicks(performance.WetMassKg, departure.Length);
                var remaining = performance.AvailablePropellantKg - firstTicks / (double)TimeSpan.TicksPerSecond * performance.RatedMassFlowKgPerSecond * firstThrottle;
                var (lastTicks, lastThrottle) = BurnTicks(performance.DryMassKg + remaining, braking.Length);
                if (firstTicks < 1 || lastTicks < 1 || firstTicks > TimeSpan.TicksPerDay || lastTicks > TimeSpan.TicksPerDay
                    || departureTicks + firstTicks >= arrivalTicks - lastTicks)
                {
                    reason = "The flight time leaves insufficient time for the required finite burns and coast.";
                    return null;
                }
                var first = CourseBurn.Build(catalogue, initial, primaryId, departureTicks, firstTicks,
                    (departure / departure.Length).Velocity, FiniteCourse.Throttled(propulsion, firstThrottle), token, validateSurface: false);
                var lastStart = arrivalTicks - lastTicks;
                var lastPropulsion = FiniteCourse.WithPropellant(propulsion, remaining);
                var to = CourseBurn.ReverseInitial(catalogue, target, arrivalPrimary, lastStart, lastTicks,
                    (braking / braking.Length).Velocity, FiniteCourse.Throttled(lastPropulsion, lastThrottle), token, validateSurface: false);
                var from = first.StateAt(first.EndTicks);
                var coastPrimary = first.PrimaryAt(first.EndTicks);
                var coastVelocity = Vector3d.From(from.VelocityKmPerSecond);
                TrajectorySegment[]? coasts = null;
                for (var correction = 0; correction < 6; correction++)
                {
                    coasts = Trace(coastVelocity);
                    var achieved = coasts[^1].StateAt(lastStart);
                    var error = Vector3d.From(to.PositionKm) - Vector3d.From(achieved.PositionKm);
                    if (error.Length < 0.001) break;
                    var step = Math.Max(1e-6, coastVelocity.Length * 1e-7);
                    var columns = new Vector3d[3];
                    for (var axis = 0; axis < 3; axis++)
                    {
                        var offset = axis switch { 0 => new Vector3d(step, 0, 0), 1 => new(0, step, 0), _ => new(0, 0, step) };
                        var displaced = Trace(coastVelocity + offset)[^1].StateAt(lastStart);
                        columns[axis] = (Vector3d.From(displaced.PositionKm) - Vector3d.From(achieved.PositionKm)) / step;
                    }
                    var determinant = Vector3d.Dot(columns[0], Vector3d.Cross(columns[1], columns[2]));
                    if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12) return null;
                    var change = new Vector3d(Vector3d.Dot(error, Vector3d.Cross(columns[1], columns[2])) / determinant,
                        Vector3d.Dot(columns[0], Vector3d.Cross(error, columns[2])) / determinant,
                        Vector3d.Dot(columns[0], Vector3d.Cross(columns[1], error)) / determinant);
                    if (!double.IsFinite(change.Length) || change.Length > 1000) return null;
                    coastVelocity += change;
                }
                coasts = Trace(coastVelocity);
                var coastEnd = coasts[^1].StateAt(lastStart);
                var departureCorrection = coastVelocity - Vector3d.From(from.VelocityKmPerSecond);
                var brakingCorrection = Vector3d.From(to.VelocityKmPerSecond) - Vector3d.From(coastEnd.VelocityKmPerSecond);
                var time = (lastStart - first.EndTicks) / (double)TimeSpan.TicksPerSecond;
                if (departureCorrection.Length * time < 0.05 && brakingCorrection.Length < 5e-7)
                {
                    try
                    {
                        var finite = FiniteCourse.Build(catalogue, new(primaryId, targetId, arrivalKind, departureTicks, arrivalTicks,
                            KinematicState.From(initial), KinematicState.From(OrbitalNavigation.ToLocal(catalogue, arrivalPrimary, arrivalTicks, target)),
                            propulsion, CartesianState.From((departure / departure.Length).Velocity), firstTicks,
                            CartesianState.From((braking / braking.Length).Velocity), lastTicks, arrivalPrimary, firstThrottle, lastThrottle), token);
                        return finite.ToPlan();
                    }
                    catch (ArgumentException) { /* Full forward verification remains authoritative. */ }
                }
                departure += departureCorrection;
                braking += brakingCorrection;

                TrajectorySegment[] Trace(Vector3d velocity) => tracer.TraceCourseCoast(new(from.PositionKm, velocity.Velocity),
                    coastPrimary, first.EndTicks, lastStart, token, validateSurface: false);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            reason = "No verified course: surface clearance, gravity transition, finite burn or convergence constraints rejected this candidate.";
        }
        return null;

        (long Ticks, double Throttle) BurnTicks(double wetMass, double deltaV)
        {
            var seconds = -wetMass * double.ExpM1(-deltaV / exhaust) / performance.RatedMassFlowKgPerSecond;
            if (seconds is not (> 0 and <= PoweredTrajectory.MaximumCourseDurationSeconds)) return (long.MaxValue / 4, 1);
            var ticks = Math.Max(1, (long)Math.Ceiling(seconds * TimeSpan.TicksPerSecond));
            // A tiny throttle reduction preserves the exact rocket-equation impulse while
            // placing cutoff on a clock tick. Thrust and mass flow decrease together.
            return (ticks, Math.Min(1, seconds / (ticks / (double)TimeSpan.TicksPerSecond)));
        }
    }
}
