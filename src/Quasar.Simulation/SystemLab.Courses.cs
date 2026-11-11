namespace Quasar.Simulation;

public sealed record CourseSearchResult(CourseChoices? Choices, string[] InfeasibleReasons);

public sealed partial class SystemLab
{
    // Capture under the session lock. Search reads only the immutable catalogue and departure inputs.
    public Func<CancellationToken, CourseSearchResult> CaptureCourseSearch(string destinationId,
        double? maneuverBudget = null, string arrivalKind = "approach", int? preferredFlightSeconds = null,
        double? reservePercent = null, bool lockFlightTime = false)
    {
        var (state, departure, _) = CaptureDeparture(destinationId, automatic: true);
        TrajectoryPlanner.ValidateManeuverBudget(maneuverBudget);
        if (arrivalKind is not ("approach" or "orbit"))
            throw new ArgumentException("Choose a velocity-matched approach or an orbit preset.");
        if (preferredFlightSeconds is < 1 or > CourseSearch.MaximumSeconds || lockFlightTime && preferredFlightSeconds is null)
            throw new ArgumentException("Choose a supported flight duration before locking travel time.");
        if (reservePercent is { } reserve && (!double.IsFinite(reserve) || reserve < 0 || reserve > 99))
            throw new ArgumentException("Choose a reserve between 0% and 99% of departure propellant.");

        var propulsion = movement.PropulsionOf(ShipId);
        if (reservePercent is { } minimum && propulsion is not null)
        {
            var performance = propulsion.CalculatePerformance();
            var finalMass = performance.DryMassKg + performance.AvailablePropellantKg * minimum / 100;
            var reserveBudget = propulsion.Engine.ExhaustVelocityMPerSecond / 1000 * Math.Log(performance.WetMassKg / finalMass);
            maneuverBudget = maneuverBudget is { } existing ? Math.Min(existing, reserveBudget) : reserveBudget;
        }
        var primary = movement.PrimaryOf(ShipId);
        var body = celestialPositions.Bodies[destinationId];
        var sphere = celestialPositions.GetEnvironment(destinationId).SphereOfInfluenceRadiusKm;
        var arrivalPrimary = arrivalKind == "orbit" ? destinationId : body.ParentId ?? destinationId;
        var unsupported = arrivalKind == "orbit" && body.GravitationalParameterKm3PerSecond2 <= 0;
        var needsPatches = arrivalPrimary != primary || destinationId == primary;

        return cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (propulsion is null) return new(null, ["This spacecraft has no modeled engine or propellant inventory."]);
            if (propulsion.Tank.QuantityKg <= 0) return new(null, ["No propellant is available for departure or braking."]);
            if (unsupported) return new(null, [arrivalKind == "orbit"
                ? "This destination has no modeled gravity for orbital insertion. Choose a velocity-matched approach."
                : "This destination has no modeled gravity primary."]);

            using var searchDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            searchDeadline.CancelAfter(TimeSpan.FromSeconds(4));
            var searchToken = searchDeadline.Token;
            var failures = new HashSet<string>(StringComparer.Ordinal);
            var approachRadius = Math.Max(body.RadiusKm * 1.1, sphere * 1.1 ?? 10);
            if (body.ParentId is null)
                approachRadius = Math.Max(approachRadius, Math.Sqrt(body.GravitationalParameterKm3PerSecond2
                    / (propulsion.CalculatePerformance().InitialAccelerationMPerSecond2 / 1000 / 100)));
            var distance = state.PositionKm.DistanceTo(celestialPositions.GetState(destinationId, departure).PositionKm);
            var estimate = Math.Clamp((int)Math.Min(CourseSearch.MaximumSeconds, distance / 100), 1, CourseSearch.MaximumSeconds);
            if (primary == arrivalPrimary)
            {
                var originRadius = Vector3d.From(OrbitalNavigation.ToLocal(celestialPositions, primary, departure, state).PositionKm).Length;
                var destinationRadius = Vector3d.From(OrbitalNavigation.ToLocal(celestialPositions, primary, departure,
                    celestialPositions.GetState(destinationId, departure)).PositionKm).Length;
                var axis = (originRadius + destinationRadius) / 2;
                var orbitalSeconds = Math.PI * Math.Sqrt(axis * axis * axis / celestialPositions.Bodies[primary].GravitationalParameterKm3PerSecond2);
                estimate = Math.Clamp((int)Math.Min(CourseSearch.MaximumSeconds, Math.Max(estimate, orbitalSeconds)), 1, CourseSearch.MaximumSeconds);
            }
            var probes = new[] { estimate, Math.Min(CourseSearch.MaximumSeconds, estimate * 2),
                Math.Min(CourseSearch.MaximumSeconds, (int)Math.Min(CourseSearch.MaximumSeconds, estimate * 4L)) };
            var patchedMode = needsPatches;
            TrajectoryPlan? Evaluate(int seconds)
            {
                var arrival = checked(departure + (long)seconds * TimeSpan.TicksPerSecond);
                var target = arrivalKind == "orbit" ? ResolvePresetOrbit(destinationId, arrival).State
                    : ApproachState(destinationId, arrivalPrimary, approachRadius, arrival, state.PositionKm);
                TrajectoryPlan? best = null;
                foreach (var longWay in new[] { false, true })
                {
                    string reason;
                    TrajectoryPlan? plan;
                    try
                    {
                        plan = patchedMode
                            ? planner.RefinePatchedCourse(state, departure, arrival, primary, arrivalPrimary, destinationId,
                                arrivalKind, target, propulsion, maneuverBudget, longWay, searchToken, out reason)
                            : planner.RefineFiniteCourse(state, departure, arrival, primary, destinationId,
                                arrivalKind, target, propulsion, maneuverBudget, longWay, searchToken, out reason);
                    }
                    catch (OperationCanceledException) when (best is not null && !cancellationToken.IsCancellationRequested && searchDeadline.IsCancellationRequested)
                    {
                        break; // Retain an already verified arc when its alternative reaches the deadline.
                    }
                    if (plan is null) failures.Add(reason);
                    else if (best is null || plan.RequiredDeltaVKmPerSecond < best.RequiredDeltaVKmPerSecond) best = plan;
                }
                return best;
            }
            var choices = CourseSearch.Find(Evaluate, cancellationToken, preferredFlightSeconds, lockFlightTime,
                () => searchDeadline.IsCancellationRequested, patchedMode ? probes : null);
            if (choices is null && !patchedMode && !searchDeadline.IsCancellationRequested)
            {
                patchedMode = true;
                choices = CourseSearch.Find(Evaluate, cancellationToken, preferredFlightSeconds, lockFlightTime,
                    () => searchDeadline.IsCancellationRequested, probes);
            }
            if (searchDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && choices is null)
                failures.Add("No verified route found within the search time limit. Try a longer flight time or unlock a constraint.");
            if (choices is null && lockFlightTime)
                failures.Add("No verified course meets the locked travel time. Unlock travel time or reduce the reserve to adjust the route.");
            return new(choices, choices is null ? [.. failures] : []);
        };
    }

    private CelestialState ApproachState(string targetId, string primaryId, double offsetKm, long ticks, Position3 referencePosition)
    {
        var target = celestialPositions.GetState(targetId, ticks);
        var local = OrbitalNavigation.ToLocal(celestialPositions, primaryId, ticks, target);
        var position = Vector3d.From(local.PositionKm);
        var velocity = Vector3d.From(local.VelocityKmPerSecond);
        if (position.Length == 0)
        {
            var reference = Vector3d.From(referencePosition) - Vector3d.From(target.PositionKm);
            var side = Vector3d.Cross(reference, new(0, 0, 1));
            if (side.Length < 1e-9) side = Vector3d.Cross(reference, new(0, 1, 0));
            var parkingRadial = side / side.Length;
            var tangent = Vector3d.Cross(Vector3d.Cross(reference, parkingRadial), parkingRadial);
            var speed = Math.Sqrt(celestialPositions.Bodies[targetId].GravitationalParameterKm3PerSecond2 / offsetKm);
            return TrajectoryPlanner.Add(target, new((parkingRadial * offsetKm).Position, (tangent / tangent.Length * speed).Velocity));
        }
        var radial = position / position.Length;
        // Offset along the moving outward radial and match the velocity of that offset point.
        var offsetVelocity = (velocity - radial * Vector3d.Dot(radial, velocity)) * (offsetKm / position.Length);
        return TrajectoryPlanner.Add(target, new((radial * offsetKm).Position, offsetVelocity.Velocity));
    }
}
