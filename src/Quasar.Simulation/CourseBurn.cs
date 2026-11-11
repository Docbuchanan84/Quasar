namespace Quasar.Simulation;

/// <summary>A finite burn with continuous state and mass across gravity-frame changes.</summary>
internal sealed class CourseBurn(CelestialPositionService catalogue, SpacecraftPropulsion propulsion,
    long startTicks, long endTicks, CourseBurn.Part[] parts)
{
    internal sealed record Part(string Primary, PoweredTrajectory Path);
    private sealed record Boundary(string Body, string Next, double Radius, bool Exit, double Acceleration);
    internal Part[] Parts => parts;
    internal long StartTicks => startTicks;
    internal long EndTicks => endTicks;
    internal bool ExhaustsPropellant => parts.Any(part => part.Path.ExhaustsPropellant);
    internal string PrimaryAt(long ticks) => parts.Last(part => part.Path.StartTicks <= ticks).Primary;
    internal double RemainingPropellantKg(long ticks) => Math.Max(0, propulsion.Tank.QuantityKg
        - (ticks - startTicks) / (double)TimeSpan.TicksPerSecond * propulsion.CalculatePerformance().RatedMassFlowKgPerSecond);
    internal CelestialState StateAt(long ticks)
    {
        var part = parts.Last(part => part.Path.StartTicks <= ticks);
        return OrbitalNavigation.ToSystem(catalogue, part.Primary, ticks, part.Path.StateAt(ticks));
    }

    internal static CourseBurn Single(CelestialPositionService catalogue, SpacecraftPropulsion propulsion,
        string primary, PoweredTrajectory path) => new(catalogue, propulsion, path.StartTicks, path.EndTicks, [new(primary, path)]);

    internal static CourseBurn Build(CelestialPositionService catalogue, CelestialState initial, string primary,
        long start, long duration, Velocity3 direction, SpacecraftPropulsion propulsion, CancellationToken token, bool validateSurface = true) =>
        Trace(catalogue, initial, primary, start, duration, direction, propulsion, false, token, validateSurface).Burn;

    internal static CelestialState ReverseInitial(CelestialPositionService catalogue, CelestialState target, string primary,
        long start, long duration, Velocity3 direction, SpacecraftPropulsion propulsion, CancellationToken token, bool validateSurface = true) =>
        Trace(catalogue, target, primary, start, duration, direction, propulsion, true, token, validateSurface).OtherEnd;

    private static (CourseBurn Burn, CelestialState OtherEnd) Trace(CelestialPositionService catalogue,
        CelestialState initial, string primary, long start, long duration, Velocity3 direction,
        SpacecraftPropulsion propulsion, bool reverse, CancellationToken token, bool validateSurface)
    {
        var end = checked(start + duration);
        var cursor = reverse ? end : start;
        var system = initial;
        var parts = new List<Part>();
        var performance = propulsion.CalculatePerformance();
        if (duration <= 0 || duration > PoweredTrajectory.MaximumCourseDurationSeconds * TimeSpan.TicksPerSecond
            || duration / (double)TimeSpan.TicksPerSecond * performance.RatedMassFlowKgPerSecond >= propulsion.Tank.QuantityKg)
            throw new ArgumentException("Burn exceeds available propellant or the supported finite duration.");
        var directionSign = reverse ? -1 : 1;
        var work = 0;
        var transitions = 0;
        string? previousPrimary = null;
        while (reverse ? cursor > start : cursor < end)
        {
            token.ThrowIfCancellationRequested();
            if (++transitions > 32) throw new InvalidOperationException("Powered gravity transition work limit exceeded.");
            var partStart = reverse ? start : cursor;
            var partEnd = reverse ? cursor : end;
            var fuel = FiniteCourse.WithPropellant(propulsion, propulsion.Tank.QuantityKg
                - (partStart - start) / (double)TimeSpan.TicksPerSecond * performance.RatedMassFlowKgPerSecond);
            var local = OrbitalNavigation.ToLocal(catalogue, primary, cursor, system);
            var mu = catalogue.Bodies[primary].GravitationalParameterKm3PerSecond2;
            var path = new PoweredTrajectory(local, direction, mu, fuel, partStart, partEnd - partStart);
            var boundaries = Boundaries(catalogue, primary, reverse).ToArray();
            var suppressed = boundaries.Where(boundary => reverse && boundary.Next == previousPrimary)
                .Select(boundary => boundary.Body).ToHashSet(StringComparer.Ordinal);
            var surface = Math.Max(1, catalogue.Bodies[primary].RadiusKm);
            (long Ticks, string Primary)? crossing = null;
            path.InspectPath(local, reverse, Inspect);
            var cutoff = crossing?.Ticks ?? (reverse ? start : end);
            var other = reverse ? path.StateBefore(local, cutoff) : path.StateAt(cutoff);
            if (cutoff != cursor)
            {
                var from = reverse ? cutoff : cursor;
                var through = reverse ? cursor : cutoff;
                var actualFuel = FiniteCourse.WithPropellant(propulsion, propulsion.Tank.QuantityKg
                    - (from - start) / (double)TimeSpan.TicksPerSecond * performance.RatedMassFlowKgPerSecond);
                parts.Add(new(primary, new PoweredTrajectory(reverse ? other : local, direction, mu,
                    actualFuel, from, through - from)));
            }
            system = OrbitalNavigation.ToSystem(catalogue, primary, cutoff, other);
            cursor = cutoff;
            if (crossing is null) break;
            previousPrimary = primary;
            primary = crossing.Value.Primary;

            bool Inspect(long ticks, CelestialState state, double step)
            {
                token.ThrowIfCancellationRequested();
                var through = ticks + (long)Math.Round(step * TimeSpan.TicksPerSecond);
                // Surface and SOI enclosures cover the continuous curve, including crossings
                // that enter and leave between RK samples. Ambiguous one-tick contacts reject.
                if (validateSurface && Find(ticks, state, through, null) is not null)
                    throw new ArgumentException("Powered course reaches a primary surface.");
                foreach (var boundary in boundaries)
                {
                    var at = Find(ticks, state, through, boundary);
                    if (at is not null && (crossing is null || directionSign * (at.Value - crossing.Value.Ticks) < 0))
                        crossing = (at.Value, boundary.Next);
                }
                return crossing is null;
            }

            long? Find(long from, CelestialState state, long through, Boundary? boundary)
            {
                token.ThrowIfCancellationRequested();
                var position = Vector3d.From(state.PositionKm);
                var velocity = Vector3d.From(state.VelocityKmPerSecond);
                var seconds = (through - from) / (double)TimeSpan.TicksPerSecond;
                var maxAcceleration = mu / Math.Pow(position.Length / 2, 2)
                    + propulsion.Engine.ThrustN / performance.DryMassKg / 1000;
                var radiusBoundHolds = velocity.Length * Math.Abs(seconds) + maxAcceleration * seconds * seconds / 2 < position.Length / 2;
                if (boundary is { Exit: false })
                {
                    var child = catalogue.OrbitalStateAt(boundary.Body, from);
                    position -= Vector3d.From(child.PositionKm);
                    velocity -= Vector3d.From(child.VelocityKmPerSecond);
                    maxAcceleration += boundary.Acceleration;
                }
                var radius = boundary?.Radius ?? surface;
                var outside = boundary?.Exit ?? false;
                if (boundary is not null && suppressed.Contains(boundary.Body))
                {
                    if (outside ? position.Length >= radius : position.Length < radius) return null;
                    suppressed.Remove(boundary.Body);
                }
                if (outside ? position.Length >= radius : position.Length < radius) return from;
                var tangent = velocity * seconds;
                var deviation = maxAcceleration * seconds * seconds / 2 + 1e-7;
                var safe = outside ? Math.Max(position.Length, (position + tangent).Length) + deviation < radius
                    : Distance(position, tangent) - deviation > radius;
                if (safe && radiusBoundHolds) return null;
                if (++work > 16384) throw new InvalidOperationException("Powered clearance subdivision work limit exceeded.");
                if (Math.Abs(through - from) <= 1)
                {
                    var final = reverse ? path.StateBefore(local, through) : path.StateAt(through);
                    var p = Vector3d.From(final.PositionKm);
                    if (boundary is { Exit: false }) p -= catalogue.OrbitalOffsetAt(boundary.Body, through);
                    if (outside ? p.Length >= radius : p.Length < radius) return through;
                    // A tangency within numerical clearance uncertainty is unsafe to certify.
                    if (Math.Abs(p.Length - radius) < 1e-7)
                    {
                        if (boundary is not null) return through; // SOI handoff within 0.1 mm of its numerical boundary.
                        throw new ArgumentException("Powered surface clearance is unresolved.");
                    }
                    return null;
                }
                var middle = from + (through - from) / 2;
                var sample = reverse ? path.StateBefore(local, middle) : path.StateAt(middle);
                return Find(from, state, middle, boundary) ?? Find(middle, sample, through, boundary);
            }
        }
        var ordered = parts.OrderBy(part => part.Path.StartTicks).ToArray();
        return (new(catalogue, propulsion, start, end, ordered), system);
    }

    private static IEnumerable<Boundary> Boundaries(CelestialPositionService catalogue, string primary, bool reverse)
    {
        if (catalogue.Bodies[primary].ParentId is { } parent
            && catalogue.Bodies[parent].GravitationalParameterKm3PerSecond2 > 0
            && catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm is { } sphere)
            yield return new(primary, parent, sphere * (reverse ? 0.99 : 1.01), true, 0);
        foreach (var child in catalogue.Bodies.Values.Where(body => body.ParentId == primary && body.Orbit is not null
            && body.GravitationalParameterKm3PerSecond2 > 0).OrderBy(body => body.Id, StringComparer.Ordinal))
        {
            var orbit = child.Orbit!;
            var radius = catalogue.GetEnvironment(child.Id).SphereOfInfluenceRadiusKm;
            if (radius is null) continue;
            var periapsis = orbit.SemiMajorAxisKm * (1 - orbit.Eccentricity);
            yield return new(child.Id, child.Id, radius.Value * (reverse ? 1.01 : 0.99), false,
                catalogue.GetEnvironment(child.Id).RelativeGravitationalParameterKm3PerSecond2!.Value / periapsis / periapsis);
        }
    }

    private static double Distance(Vector3d start, Vector3d displacement)
    {
        var square = Vector3d.Dot(displacement, displacement);
        var fraction = square > 0 ? Math.Clamp(-Vector3d.Dot(start, displacement) / square, 0, 1) : 0;
        return (start + displacement * fraction).Length;
    }
}
