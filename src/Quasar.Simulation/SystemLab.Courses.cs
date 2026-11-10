namespace Quasar.Simulation;

public sealed record CourseSearchResult(CourseChoices? Choices, string[] InfeasibleReasons);

public sealed partial class SystemLab
{
    // Capture under the session lock. Search reads only the immutable catalogue and departure inputs.
    public Func<CancellationToken, CourseSearchResult> CaptureCourseSearch(string destinationId,
        double? maneuverBudget = null, string arrivalKind = "approach", int? preferredFlightSeconds = null,
        double? reservePercent = null, bool lockFlightTime = false)
    {
        var (state, departure, _) = CaptureDeparture(destinationId);
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
        var unsupported = arrivalKind == "orbit"
            ? destinationId is not (SolarSystem.Earth or "moon-moon") || primary != destinationId
            : body.ParentId != primary || sphere is null;

        return cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (propulsion is null) return new(null, ["This spacecraft has no modeled engine or propellant inventory."]);
            if (propulsion.Tank.QuantityKg <= 0) return new(null, ["No propellant is available for departure or braking."]);
            if (unsupported) return new(null, [arrivalKind == "orbit"
                ? "Orbit insertion supports the Earth and Moon presets from within that body's gravity primary. Cross-primary capture is unsupported."
                : "This approach requires an unsupported gravity-primary handoff. Choose a planet or moon that orbits the spacecraft's current primary."]);

            var failures = new HashSet<string>(StringComparer.Ordinal);
            var choices = CourseSearch.Find(seconds =>
            {
                var arrival = checked(departure + (long)seconds * TimeSpan.TicksPerSecond);
                var target = arrivalKind == "orbit" ? ResolvePresetOrbit(destinationId, arrival).State
                    : ApproachState(destinationId, primary, sphere!.Value * 1.1, arrival);
                TrajectoryPlan? best = null;
                foreach (var longWay in new[] { false, true })
                {
                    var plan = planner.RefineFiniteCourse(state, departure, arrival, primary, destinationId,
                        arrivalKind, target, propulsion, maneuverBudget, longWay, cancellationToken, out var reason);
                    if (plan is null) failures.Add(reason);
                    else if (best is null || plan.RequiredDeltaVKmPerSecond < best.RequiredDeltaVKmPerSecond) best = plan;
                }
                return best;
            }, cancellationToken, preferredFlightSeconds, lockFlightTime);
            if (choices is null && lockFlightTime)
                failures.Add("No verified course meets the locked travel time. Unlock travel time or reduce the reserve to adjust the route.");
            return new(choices, choices is null ? [.. failures] : []);
        };
    }

    private CelestialState ApproachState(string targetId, string primaryId, double offsetKm, long ticks)
    {
        var target = celestialPositions.GetState(targetId, ticks);
        var local = OrbitalNavigation.ToLocal(celestialPositions, primaryId, ticks, target);
        var position = Vector3d.From(local.PositionKm);
        var velocity = Vector3d.From(local.VelocityKmPerSecond);
        var radial = position / position.Length;
        // Offset along the moving outward radial and match the velocity of that offset point.
        var offsetVelocity = (velocity - radial * Vector3d.Dot(radial, velocity)) * (offsetKm / position.Length);
        return TrajectoryPlanner.Add(target, new((radial * offsetKm).Position, offsetVelocity.Velocity));
    }
}
