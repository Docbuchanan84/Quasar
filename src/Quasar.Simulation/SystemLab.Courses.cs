namespace Quasar.Simulation;

public sealed record CourseSearchResult(CourseChoices? Choices, string[] InfeasibleReasons);

public sealed partial class SystemLab
{
    // Capture under the session lock. Search reads only the immutable catalogue and departure inputs.
    public Func<CancellationToken, CourseSearchResult> CaptureCourseSearch(string destinationId,
        double? maneuverBudget = null, string arrivalKind = "approach")
    {
        var (state, departure, _) = CaptureDeparture(destinationId);
        TrajectoryPlanner.ValidateManeuverBudget(maneuverBudget);
        if (arrivalKind is not ("approach" or "orbit"))
            throw new ArgumentException("Choose a velocity-matched approach or an orbit preset.");

        var propulsion = movement.PropulsionOf(ShipId);
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
            }, cancellationToken);
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
