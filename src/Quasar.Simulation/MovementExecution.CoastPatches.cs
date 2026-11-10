namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    private const double BoundaryToleranceKm = 1e-5;
    private const long SearchWindow = 30 * TimeSpan.TicksPerDay;
    private readonly record struct CoastSample(CelestialState Local, Vector3d RelativePosition);
    private sealed record CoastPatch(long AtTicks, string Primary, TwoBodyTrajectory Coast);
    private sealed record SphereBoundary(string BodyId, string NextPrimary, double RadiusKm, bool IsExit, double ChildAcceleration = 0);

    internal string PrimaryOf(string id) => spacecraft[id].Primary;

    internal bool CourseCrossesPrimaryBoundary(FiniteCourse course, CancellationToken cancellationToken)
    {
        var primary = course.Coast.PrimaryBodyId;
        var coast = course.Coast.Coast;
        var minimumRadius = Math.Max(1, catalogue.Bodies[primary].RadiusKm);
        var parentSphere = catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm;
        var children = catalogue.Bodies.Values.Where(body => body.ParentId == primary && body.Orbit is not null
                && body.GravitationalParameterKm3PerSecond2 > 0)
            .Select(body => (body.Id, Sphere: catalogue.GetEnvironment(body.Id).SphereOfInfluenceRadiusKm))
            .Where(body => body.Sphere is > 0).ToArray();
        var outsideModel = false;
        course.Departure.InspectPath(Inspect);
        course.Braking.InspectPath(Inspect);
        if (outsideModel) return true;

        for (var from = course.Coast.StartTicks; from < course.Coast.EndTicks;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var through = from + Math.Min(SearchWindow, course.Coast.EndTicks - from);
            if (FindCoastPatch(coast, primary, from, through, cancellationToken) is not null) return true;
            from = through;
        }
        return false;

        void Inspect(long ticks, Position3 position)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outsideModel) return;
            var radius = Vector3d.From(position).Length;
            outsideModel = radius <= minimumRadius || radius >= parentSphere
                || children.Any(child => position.DistanceTo(catalogue.OrbitalOffsetAt(child.Id, ticks).Position) <= child.Sphere);
        }
    }

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

    private CoastPatch? FindCoastPatch(TwoBodyTrajectory coast, string primary, long from, long through,
        CancellationToken? courseCancellation = null)
    {
        if (through < from) return null;
        CoastPatch? chosen = null;
        var samples = 0;
        foreach (var boundary in SphereBoundaries(coast, primary))
        {
            // Exit precedes ordered child IDs, so only an earlier tick can replace the winner.
            if (chosen?.AtTicks == from) break;
            var limit = chosen is null ? through : chosen.AtTicks - 1;
            var radius = boundary.RadiusKm + (boundary.IsExit ? BoundaryToleranceKm : -BoundaryToleranceKm);
            if (radius <= 0) continue;
            var left = SampleAt(from);
            var right = SampleAt(limit);
            var cursor = from;

            // A coast created inside a child (or outside its primary) can patch at its epoch.
            // Otherwise find a fresh crossing, including after an unsupported radial handoff.
            var allowCurrent = from == coast.EpochTicks;
            while (cursor <= limit)
            {
                if (!allowCurrent && OnSide(left.RelativePosition, boundary.IsExit))
                {
                    var opposite = FirstOnSide(cursor, left, limit, right, !boundary.IsExit);
                    if (opposite is null) break;
                    cursor = opposite.Value;
                    left = SampleAt(cursor);
                }
                var crossing = FirstOnSide(cursor, left, limit, right, boundary.IsExit);
                if (crossing is null) break;
                if (courseCancellation.HasValue)
                    return new(crossing.Value, boundary.NextPrimary, coast);
                var system = OrbitalNavigation.ToSystem(catalogue, primary, crossing.Value, coast.StateAt(crossing.Value));
                var local = OrbitalNavigation.ToLocal(catalogue, boundary.NextPrimary, crossing.Value, system);
                try
                {
                    var next = new TwoBodyTrajectory(local, crossing.Value, PrimaryMu(boundary.NextPrimary));
                    chosen = new(crossing.Value, boundary.NextPrimary, next);
                    break;
                }
                catch (ArgumentException)
                {
                    // A centre-directed course can leave a radial state the local solver cannot propagate.
                    cursor = crossing.Value;
                    left = SampleAt(cursor);
                    allowCurrent = false;
                }
            }

            CoastSample SampleAt(long ticks)
            {
                if (courseCancellation is { } token)
                {
                    token.ThrowIfCancellationRequested();
                    if (++samples > 8192) throw new InvalidOperationException("Course gravity-boundary check exceeded its work limit.");
                }
                var local = coast.StateAt(ticks);
                var position = Vector3d.From(local.PositionKm);
                return new(local, boundary.IsExit ? position : position - catalogue.OrbitalOffsetAt(boundary.BodyId, ticks));
            }

            bool OnSide(Vector3d position, bool outside) => outside ? position.Length >= radius : position.Length < radius;

            long? FirstOnSide(long low, CoastSample lowSample, long high, CoastSample highSample, bool outside)
            {
                var lowPosition = lowSample.RelativePosition;
                var highPosition = highSample.RelativePosition;
                if (OnSide(lowPosition, outside)) return low;
                if (low == high) return null;
                if (high - low == 1) return OnSide(highPosition, outside) ? high : null;

                // A curve with acceleration bounded by A stays within A * dt² / 8 of its chord.
                // Reject an interval only if the whole chord, plus that error, misses the boundary.
                var seconds = (double)(high - low) / TimeSpan.TicksPerSecond;
                var acceleration = coast.MaximumAccelerationFrom(lowSample.Local, seconds) + boundary.ChildAcceleration;
                var error = acceleration * seconds * seconds / 8 + BoundaryToleranceKm / 10;
                if (outside)
                {
                    if (Math.Max(lowPosition.Length, highPosition.Length) + error < radius) return null;
                }
                else
                {
                    var chord = highPosition - lowPosition;
                    var lengthSquared = Vector3d.Dot(chord, chord);
                    var nearest = lengthSquared > 0 ? Math.Clamp(-Vector3d.Dot(lowPosition, chord) / lengthSquared, 0, 1) : 0;
                    if ((lowPosition + chord * nearest).Length - error >= radius) return null;
                }

                var mid = low + (high - low) / 2;
                var midSample = SampleAt(mid);
                return FirstOnSide(low, lowSample, mid, midSample, outside)
                    ?? FirstOnSide(mid, midSample, high, highSample, outside);
            }
        }
        return chosen;
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
            if (child.Orbit is not { } orbit) continue;
            var periapsis = orbit.SemiMajorAxisKm * (1 - orbit.Eccentricity);
            if (coast.PeriapsisKm > orbit.SemiMajorAxisKm * (1 + orbit.Eccentricity) + entry
                || coast.ApoapsisKm < periapsis - entry) continue;
            var mu = catalogue.GetEnvironment(child.Id).RelativeGravitationalParameterKm3PerSecond2!.Value;
            yield return new(child.Id, child.Id, entry, false, mu / periapsis / periapsis);
        }
    }
}
