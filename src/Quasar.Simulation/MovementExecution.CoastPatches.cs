namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    private const double BoundaryToleranceKm = 1e-5;
    private const long SearchWindow = 30 * TimeSpan.TicksPerDay;
    private readonly record struct CoastSample(CelestialState Local, Vector3d RelativePosition);
    private sealed record CoastPatch(long AtTicks, string Primary, TwoBodyTrajectory Coast);
    private readonly record struct SphereBoundary(string BodyId, string NextPrimary, double RadiusKm, bool IsExit, double ChildAcceleration = 0);

    internal string PrimaryOf(string id) => spacecraft[id].Primary;

    internal bool CourseCrossesPrimaryBoundary(FiniteCourse course, CancellationToken cancellationToken)
    {
        if (course.State.ArrivalPrimaryBodyId is not null) return false; // Rebuilt with validated continuous patched paths.
        var primary = course.Coast.PrimaryBodyId;
        var coast = course.Coast.Coast;
        if (!PoweredPathIsClear(course.Departure.Parts[0].Path, primary, cancellationToken)
            || !PoweredPathIsClear(course.Braking.Parts[0].Path, primary, cancellationToken)) return true;

        for (var from = course.Coast.StartTicks; from < course.Coast.EndTicks;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var through = from + Math.Min(SearchWindow, course.Coast.EndTicks - from);
            if (FindCoastPatch(coast, primary, from, through, cancellationToken) is not null) return true;
            from = through;
        }
        return false;
    }

    internal bool PoweredPathIsClear(PoweredTrajectory path, string primary, CancellationToken cancellationToken)
    {
        var minimumRadius = Math.Max(1, catalogue.Bodies[primary].RadiusKm);
        var parentSphere = catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm;
        var children = catalogue.Bodies.Values.Where(body => body.ParentId == primary && body.Orbit is not null
                && body.GravitationalParameterKm3PerSecond2 > 0)
            .Select(body =>
            {
                var orbit = body.Orbit!;
                var environment = catalogue.GetEnvironment(body.Id);
                var sphere = environment.SphereOfInfluenceRadiusKm!.Value;
                var periapsis = orbit.SemiMajorAxisKm * (1 - orbit.Eccentricity);
                var apoapsis = orbit.SemiMajorAxisKm * (1 + orbit.Eccentricity);
                return (body.Id, Sphere: sphere, Inner: periapsis - sphere, Outer: apoapsis + sphere,
                    Acceleration: environment.RelativeGravitationalParameterKm3PerSecond2!.Value / periapsis / periapsis);
            }).ToArray();
        return path.HasClearPath(Inspect);

        bool Inspect(long ticks, Vector3d position, Vector3d velocity, double step, double deviation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The continuous path lies within ½ A t² of the initial tangent. Work in the
            // child's moving frame by adding its maximum orbital acceleration to that bound.
            var tangent = velocity * step;
            var lowerRadius = DistanceToSegment(position, tangent) - deviation - BoundaryToleranceKm;
            var upperRadius = Math.Max(position.Length, (position + tangent).Length) + deviation + BoundaryToleranceKm;
            if (!(lowerRadius > minimumRadius) || upperRadius >= parentSphere) return false;
            foreach (var child in children)
            {
                if (upperRadius < child.Inner || lowerRadius > child.Outer) continue;
                var center = catalogue.OrbitalStateAt(child.Id, ticks);
                var relative = position - Vector3d.From(center.PositionKm);
                var relativeTangent = tangent - Vector3d.From(center.VelocityKmPerSecond) * step;
                var relativeDeviation = deviation + child.Acceleration * step * step / 2 + BoundaryToleranceKm;
                if (!(DistanceToSegment(relative, relativeTangent) > child.Sphere + relativeDeviation)) return false;
            }
            return true;
        }
    }

    private static double DistanceToSegment(Vector3d start, Vector3d displacement)
    {
        var lengthSquared = Vector3d.Dot(displacement, displacement);
        var nearest = lengthSquared > 0 ? Math.Clamp(-Vector3d.Dot(start, displacement) / lengthSquared, 0, 1) : 0;
        return (start + displacement * nearest).Length;
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
        if (ship.Plan?.Finite is { } course && atTicks > course.Braking.EndTicks)
        {
            var (primary, state) = PredictCoast(course.ArrivalCoast, course.Arrival.PrimaryBodyId, atTicks, course.Braking.EndTicks);
            return new("spacecraft", id, primary, atTicks, state);
        }
        var primaryId = ship.Plan?.Finite?.PrimaryAt(atTicks)
            ?? ship.Plan?.Segments.LastOrDefault(segment => segment.StartTicks <= atTicks)?.PrimaryBodyId ?? ship.Primary;
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
        CancellationToken? courseCancellation = null, bool buildPatch = false)
    {
        if (through < from) return null;
        CoastPatch? chosen = null;
        var samples = 0;
        if (catalogue.Bodies[primary].ParentId is { } parent
            && catalogue.Bodies[parent].GravitationalParameterKm3PerSecond2 > 0
            && catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm is { } sphere
            && (coast.ApoapsisKm is not { } apoapsis || apoapsis >= sphere * 1.01))
        {
            if (ConsiderBoundary(new(primary, parent, sphere * 1.01, true)) is { } crossing) return crossing;
        }
        foreach (var child in catalogue.GravityChildren(primary))
        {
            // Exit precedes ordered child IDs, so only an earlier tick can replace the winner.
            if (chosen?.AtTicks == from) break;
            var environment = catalogue.GetEnvironment(child.Id);
            var entry = environment.SphereOfInfluenceRadiusKm!.Value * 0.99;
            var orbit = child.Orbit!;
            var periapsis = orbit.SemiMajorAxisKm * (1 - orbit.Eccentricity);
            if (coast.PeriapsisKm > orbit.SemiMajorAxisKm * (1 + orbit.Eccentricity) + entry
                || coast.ApoapsisKm < periapsis - entry) continue;
            var acceleration = environment.RelativeGravitationalParameterKm3PerSecond2!.Value / periapsis / periapsis;
            if (ConsiderBoundary(new(child.Id, child.Id, entry, false, acceleration)) is { } crossing) return crossing;
        }
        return chosen;

        CoastPatch? ConsiderBoundary(SphereBoundary boundary)
        {
            var limit = chosen is null ? through : chosen.AtTicks - 1;
            var radius = boundary.RadiusKm + (boundary.IsExit ? BoundaryToleranceKm : -BoundaryToleranceKm);
            if (radius <= 0) return null;
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
                if (courseCancellation.HasValue && !buildPatch)
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
                    if (buildPatch) throw new ArgumentException("Gravity transition creates an unsupported radial coast state.");
                    // A centre-directed course can leave a radial state the local solver cannot propagate.
                    cursor = crossing.Value;
                    left = SampleAt(cursor);
                    allowCurrent = false;
                }
            }
            return null;

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
                    if (DistanceToSegment(lowPosition, highPosition - lowPosition) - error >= radius) return null;
                }

                var mid = low + (high - low) / 2;
                var midSample = SampleAt(mid);
                return FirstOnSide(low, lowSample, mid, midSample, outside)
                    ?? FirstOnSide(mid, midSample, high, highSample, outside);
            }
        }
    }
}
