namespace Quasar.Simulation;

public sealed partial class TrajectoryPlanner
{
    public TrajectoryPlan PlanWithFlyby(CelestialState initial, long departure, TransferLeg arrival, double budget)
    {
        if (!double.IsFinite(budget) || budget is < 1 or > 100)
            throw new ArgumentException("Choose a maneuver budget between 1 and 100 km/s.");

        var best = TryPlan([arrival]);
        var alternate = TryPlan([arrival with { LongWay = !arrival.LongWay }]);
        if (alternate is not null && (best is null || alternate.RequiredDeltaVKmPerSecond < best.RequiredDeltaVKmPerSecond))
            best = alternate;
        foreach (var body in catalogue.Bodies.Values.OrderBy(body => body.Id, StringComparer.Ordinal))
        {
            if (body.Kind != CelestialBodyKind.Planet || body.ParentId != arrival.PrimaryBodyId
                || body.Id == arrival.Destination.BodyId || body.RadiusKm <= 0 || body.GravitationalParameterKm3PerSecond2 <= 0)
                continue;
            Search(body, false, false);
            Search(body, false, true);
            Search(body, true, false);
            Search(body, true, true);
        }

        if (best is null || best.RequiredDeltaVKmPerSecond > budget)
            throw new ArgumentException("No direct or single-flyby route found within this flight time and maneuver budget.");
        return best;

        void Search(CelestialBody body, bool firstLongWay, bool secondLongWay)
        {
            (long At, double Difference)? previous = null;
            var duration = (decimal)arrival.ArrivalTicks - departure;
            for (var i = 0; i < 48; i++)
            {
                var at = departure + (long)(duration * (.05m + .90m * i / 47));
                var difference = Evaluate(at);
                if (difference is { } current && previous is { } prior && Math.Sign(current) != Math.Sign(prior.Difference))
                {
                    var low = prior.At;
                    var high = at;
                    for (var iteration = 0; iteration < 28 && high - low > 1; iteration++)
                    {
                        var middle = low + (high - low) / 2;
                        var value = Evaluate(middle);
                        if (value is null || Math.Abs(value.Value) <= 1e-7) break;
                        if (Math.Sign(value.Value) == Math.Sign(prior.Difference)) low = middle;
                        else high = middle;
                    }
                }
                previous = difference is { } valid ? (at, valid) : null;
            }

            double? Evaluate(long at)
            {
                var encounter = arrival with { ArrivalTicks = at, Destination = new(body.Id), LongWay = firstLongWay };
                var plan = TryPlan([encounter, arrival with { LongWay = secondLongWay }]);
                if (plan is null) return null;
                var incoming = plan.Segments[0].StateAt(at);
                var outgoing = plan.Segments[1].StateAt(at);
                var planet = catalogue.GetState(body.Id, at);
                var a = Vector3d.From(incoming.VelocityKmPerSecond) - Vector3d.From(planet.VelocityKmPerSecond);
                var b = Vector3d.From(outgoing.VelocityKmPerSecond) - Vector3d.From(planet.VelocityKmPerSecond);
                var difference = a.Length - b.Length;
                if (Math.Abs(difference) <= .001 && ClosestApproach(body, a, b) is { } radius)
                {
                    var cost = plan.Burns[0].DeltaVKmPerSecond + plan.Burns[^1].DeltaVKmPerSecond;
                    if (cost <= budget && (best is null || best.RequiredDeltaVKmPerSecond > cost + 1e-7))
                        best = new(catalogue, initial, arrival.Destination, [.. plan.Segments], [.. plan.Burns],
                            fuelEstimator?.Estimate(cost) ?? plan.Fuel, new(body.Id, radius));
                }
                return difference;
            }
        }

        TrajectoryPlan? TryPlan(TransferLeg[] legs)
        {
            try { return PlanPatched(initial, departure, legs); }
            catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException) { return null; }
        }
    }

    private static double? ClosestApproach(CelestialBody body, Vector3d a, Vector3d b)
    {
        if (a.Length <= 0 || b.Length <= 0) return null;
        // For an unpowered hyperbolic encounter, sin(turn/2) = 1 / eccentricity.
        var halfTurn = ((a / a.Length) - (b / b.Length)).Length / 2;
        if (halfTurn <= 0) return null;
        var radius = body.GravitationalParameterKm3PerSecond2 / (a.Length * b.Length) * (1 / halfTurn - 1);
        return double.IsFinite(radius) && radius >= body.RadiusKm + 1000 ? radius : null;
    }

    internal static bool IsValidFlyby(CelestialPositionService catalogue, FlybyEncounter encounter, TrajectorySegment[] segments)
    {
        if (encounter.BodyId is null || segments.Length != 2 || segments[0].PrimaryBodyId != segments[1].PrimaryBodyId
            || !catalogue.Bodies.TryGetValue(encounter.BodyId, out var body)
            || body.Kind != CelestialBodyKind.Planet || body.ParentId != segments[0].PrimaryBodyId)
            return false;
        var at = segments[1].StartTicks;
        var planet = catalogue.GetState(body.Id, at);
        var incoming = segments[0].StateAt(at);
        var outgoing = segments[1].StateAt(at);
        if (outgoing.PositionKm.DistanceTo(planet.PositionKm) > Math.Max(1e-5, Vector3d.From(planet.PositionKm).Length * 1e-8))
            return false;
        var a = Vector3d.From(incoming.VelocityKmPerSecond) - Vector3d.From(planet.VelocityKmPerSecond);
        var b = Vector3d.From(outgoing.VelocityKmPerSecond) - Vector3d.From(planet.VelocityKmPerSecond);
        return Math.Abs(a.Length - b.Length) <= .001 && ClosestApproach(body, a, b) is { } radius
            && Math.Abs(radius - encounter.ClosestApproachKm) <= radius * 1e-8;
    }
}
