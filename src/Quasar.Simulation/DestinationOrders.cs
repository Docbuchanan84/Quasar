namespace Quasar.Simulation;

public sealed record DestinationOrder(string ShipId, string DestinationId, string ArrivalKind,
    double? ManeuverBudget = null, int? PreferredFlightSeconds = null, double? ReservePercent = null, bool LockFlightTime = false);
public sealed record NavigationOptions(IReadOnlyList<string> ArrivalKinds, string EfficiencyMetric, bool AdvancedConstraints,
    bool CanPlot = true, bool HasCourse = false);

public sealed record NavigationCandidate(TrajectoryDisplay Trajectory, double OperatingCost, Action Commit)
{
    public double DistanceKm => Trajectory.Points.Zip(Trajectory.Points.Skip(1),
        (a, b) => a.PositionKm.DistanceTo(b.PositionKm)).Sum();
}

public sealed record DestinationRoutes(NavigationCandidate? Fastest, NavigationCandidate? Efficient,
    NavigationCandidate? Direct, NavigationCandidate? Tuned, string[] Reasons)
{
    internal static DestinationRoutes Rank(IEnumerable<NavigationCandidate> candidates, string[] reasons,
        NavigationCandidate? tuned = null)
    {
        var routes = candidates.Distinct().ToArray();
        return new(routes.MinBy(route => (route.Trajectory.TravelSeconds, route.OperatingCost)),
            routes.MinBy(route => (route.OperatingCost, route.Trajectory.TravelSeconds)),
            routes.MinBy(route => (route.Trajectory.AssistBodies?.Length ?? (route.Trajectory.Flyby is null ? 0 : 1),
                route.DistanceKm, route.Trajectory.TravelSeconds)), tuned, reasons);
    }
}

internal sealed record DestinationPlanningContext(SystemLab Lab, DestinationOrder Order);

public sealed partial class SystemLab
{
    public Func<CancellationToken, DestinationRoutes> CaptureDestinationOrder(DestinationOrder order)
    {
        if (!movement.ContainsSpacecraft(order.ShipId))
            throw new ArgumentException("Choose a spacecraft in the lab.");
        if (!HasModeledPropulsion && order.ShipId == ShipId && movement.PlanningComponentOf(order.ShipId) is null)
            return CaptureRocketDestination(order);
        var component = movement.PlanningComponentOf(order.ShipId)
            ?? throw new InvalidOperationException("This spacecraft has no installed propulsion planner.");
        return component.CaptureDestination(new(this, order));
    }

    internal Func<CancellationToken, DestinationRoutes> CaptureRocketDestination(DestinationOrder order)
    {
        var search = CaptureCourseSearchForSpacecraft(order.ShipId, order.DestinationId, order.ManeuverBudget,
            order.ArrivalKind, order.PreferredFlightSeconds, order.ReservePercent, order.LockFlightTime);
        return token =>
        {
            var result = search(token);
            if (result.Choices is not { } choices) return new(null, null, null, null, result.InfeasibleReasons);
            var candidates = new Dictionary<TrajectoryPlan, NavigationCandidate>();
            NavigationCandidate Adapt(TrajectoryPlan plan)
            {
                if (candidates.TryGetValue(plan, out var candidate)) return candidate;
                var display = Present(plan);
                candidate = new(display, display.Fuel.PropellantMassKg ?? plan.RequiredDeltaVKmPerSecond,
                    () => CommitTravel(order.ShipId, plan));
                candidates.Add(plan, candidate);
                return candidate;
            }
            var tuned = choices.Tuned is { } selected ? Adapt(selected) : null;
            var alternatives = new[] { Adapt(choices.Fastest), Adapt(choices.LowestManeuverCost),
                Adapt(choices.Direct ?? choices.Fastest) };
            return DestinationRoutes.Rank(alternatives, [], tuned);
        };
    }
}
