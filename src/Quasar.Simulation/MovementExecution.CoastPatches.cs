namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    private const long SampleTicks = TimeSpan.TicksPerMinute;
    private const long SearchWindow = 30 * TimeSpan.TicksPerDay;
    private sealed record CoastPatch(long AtTicks, string Primary, TwoBodyTrajectory Coast);
    private sealed record SphereBoundary(string BodyId, string NextPrimary, double RadiusKm, bool IsExit);

    internal string PrimaryOf(string id) => spacecraft[id].Primary;

    public OrbitalTargetState ResolveSpacecraftTarget(string id, long atTicks)
    {
        if (!spacecraft.TryGetValue(id, out var ship))
            throw new ArgumentException("Choose a modeled spacecraft.", nameof(id));
        RequireFuture(atTicks);
        if (ship.Coast is { } coast && ship.AutomaticPrimary)
        {
            var (primary, state) = PredictCoast(coast, ship.Primary, atTicks);
            return new("spacecraft", id, primary, atTicks, state);
        }

        if (ship.Burn is { } burn && atTicks > burn.Path.EndTicks)
        {
            var (primary, state) = PredictCoast(burn.EndCoast, ship.Primary, atTicks, burn.Path.EndTicks);
            return new("spacecraft", id, primary, atTicks, state);
        }
        var primaryId = ship.Plan?.Segments.LastOrDefault(segment => segment.StartTicks <= atTicks)?.PrimaryBodyId ?? ship.Primary;
        return new("spacecraft", id, primaryId, atTicks, StateAt(id, atTicks));
    }

    private (string Primary, CelestialState State) PredictCoast(TwoBodyTrajectory coast, string primary, long atTicks, long? from = null)
    {
        var cursor = from ?? ElapsedTicks;
        if ((decimal)atTicks - cursor > SearchWindow)
            throw new ArgumentOutOfRangeException(nameof(atTicks), "Automatic coast predictions cover up to 30 days.");
        for (var count = 0; atTicks > cursor; count++)
        {
            var patch = FindCoastPatch(coast, primary, cursor, atTicks);
            if (patch is null) break;
            if (count == 64) throw new InvalidOperationException("Too many gravity transitions in one prediction.");
            (cursor, primary, coast) = (patch.AtTicks, patch.Primary, patch.Coast);
        }
        return (primary, OrbitalNavigation.ToSystem(catalogue, primary, atTicks, coast.StateAt(atTicks)));
    }

    private CoastPatch? FindCoastPatch(TwoBodyTrajectory coast, string primary, long from, long through)
    {
        if (through < from) return null;
        var boundaries = SphereBoundaries(coast, primary).ToArray();
        if (boundaries.Length == 0) return null;

        // Use the same bracketing endpoints even when the requested interval ends between them.
        var elapsed = (decimal)from - coast.EpochTicks;
        var left = from - (long)(elapsed % SampleTicks);
        while (left <= through)
        {
            var right = left > long.MaxValue - SampleTicks ? long.MaxValue : left + SampleTicks;
            var leftState = coast.StateAt(left);
            var rightState = coast.StateAt(right);
            CoastPatch? chosen = null;
            var first = through;
            foreach (var boundary in boundaries)
            {
                long crossing;
                if (left == coast.EpochTicks && Reached(boundary, primary, left, leftState))
                    crossing = left;
                else if (!Reached(boundary, primary, left, leftState) && Reached(boundary, primary, right, rightState))
                {
                    var low = left;
                    var high = right;
                    while (high - low > 1)
                    {
                        var mid = low + (high - low) / 2;
                        if (Reached(boundary, primary, mid, coast.StateAt(mid))) high = mid;
                        else low = mid;
                    }
                    crossing = high;
                }
                else continue;

                if (crossing < from || crossing > first || (crossing == first && chosen is not null)) continue;
                var system = OrbitalNavigation.ToSystem(catalogue, primary, crossing, coast.StateAt(crossing));
                var local = OrbitalNavigation.ToLocal(catalogue, boundary.NextPrimary, crossing, system);
                TwoBodyTrajectory nextCoast;
                try
                {
                    nextCoast = new(local, crossing, PrimaryMu(boundary.NextPrimary));
                }
                catch (ArgumentException)
                {
                    // A centre-directed legacy course can leave a radial state the local solver cannot propagate.
                    continue;
                }
                first = crossing;
                chosen = new(crossing, boundary.NextPrimary, nextCoast);
            }
            if (chosen is not null) return chosen;
            if (right >= through) break;
            left = right;
        }
        return null;
    }

    private IEnumerable<SphereBoundary> SphereBoundaries(TwoBodyTrajectory coast, string primary)
    {
        if (catalogue.Bodies[primary].ParentId is { } parent
            && catalogue.Bodies[parent].GravitationalParameterKm3PerSecond2 > 0
            && catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm is { } sphere
            && (coast.ApoapsisKm is not { } apoapsis || apoapsis >= sphere * 1.01))
            yield return new(primary, parent, sphere * 1.01, true);

        foreach (var child in catalogue.Bodies.Values.Where(body => body.ParentId == primary).OrderBy(body => body.Id, StringComparer.Ordinal))
        {
            if (catalogue.GetEnvironment(child.Id).SphereOfInfluenceRadiusKm is not { } radius
                || child.GravitationalParameterKm3PerSecond2 <= 0) continue;
            var entry = radius * 0.99;
            if (child.Orbit is { } orbit
                && (coast.PeriapsisKm > orbit.SemiMajorAxisKm * (1 + orbit.Eccentricity) + entry
                    || coast.ApoapsisKm < orbit.SemiMajorAxisKm * (1 - orbit.Eccentricity) - entry)) continue;
            yield return new(child.Id, child.Id, entry, false);
        }
    }

    private bool Reached(SphereBoundary boundary, string primary, long ticks, CelestialState local)
    {
        if (boundary.IsExit)
            return Vector3d.From(local.PositionKm).Length >= boundary.RadiusKm;
        var child = OrbitalNavigation.ToLocal(catalogue, primary, ticks, catalogue.GetState(boundary.BodyId, ticks));
        return local.PositionKm.DistanceTo(child.PositionKm) <= boundary.RadiusKm;
    }
}
