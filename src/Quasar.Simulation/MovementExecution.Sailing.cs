using System.Text.Json.Serialization;

namespace Quasar.Simulation;

public sealed record SailStatus(string Status, string PrimaryBodyId, double SpeedKmPerSecond, double SolarDistanceAu,
    double MassKg, double AreaSquareMeters, double FaceOnAccelerationMPerSecond2, double? AngleDegrees,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? TrimEndTicks);
public sealed record SailFlyby(string BodyId, string BodyName, double ClosestAltitudeKm, double? SolarSpeedChangeKmPerSecond);
public sealed record SailFlightPath(TrajectoryDisplayPoint[] Points);
public sealed record SailPreview(SailFlightPath? Coast, SailFlightPath? Trim, SailFlyby[] CoastFlybys,
    SailFlyby[] TrimFlybys, string? CoastWarning = null);

public sealed partial class MovementExecution
{
    public void AddSailcraft(string id, CelestialState state, string primary, SolarSail sail)
    {
        ArgumentNullException.ThrowIfNull(sail);
        if (!catalogue.Bodies.TryGetValue(SolarSystem.Sun, out var sun) || sun.Kind != CelestialBodyKind.Star)
            throw new InvalidOperationException("Solar sails require the modeled Sun.");
        AddShip(id, state, primary, sail, automaticPrimary: true);
    }

    internal bool ContainsSpacecraft(string id) => spacecraft.ContainsKey(id);

    internal SailStatus SailView(string id)
    {
        var ship = spacecraft[id];
        var sail = RequireSail(ship);
        var state = StateAt(id, ElapsedTicks);
        var relative = OrbitalNavigation.ToLocal(catalogue, SolarSystem.Sun, ElapsedTicks, state);
        var distance = Vector3d.From(relative.PositionKm).Length;
        return new(ship.Sailing is null ? "Coasting" : "Sailing", ship.Primary,
            Vector3d.From(state.VelocityKmPerSecond).Length, distance / SolarSail.AstronomicalUnitKm,
            sail.TotalMassKg, sail.AreaSquareMeters, sail.FaceOnAcceleration(distance),
            ship.Sailing?.Order.AngleDegrees, ship.Sailing?.EndTicks);
    }

    internal SailPreview PreviewSail(string id, double? angleDegrees, int durationSeconds, CancellationToken cancellationToken)
    {
        var ship = spacecraft[id];
        RequireSail(ship);
        if (ship.Coast is not { } current)
            throw new InvalidOperationException("Feather the sail before previewing another trim.");
        var start = ElapsedTicks;
        var initial = OrbitalNavigation.ToSystem(catalogue, ship.Primary, start, current.StateAt(start));
        var trim = angleDegrees is { } angle ? CreateTrim(ship, angle, durationSeconds, cancellationToken) : null;
        var through = checked((trim?.EndTicks ?? start) + 7 * TimeSpan.TicksPerDay);
        var baseline = TraceCourseCoast(initial, ship.Primary, start, through, cancellationToken, validateSurface: false);
        var safeCoast = baseline.All(segment => CoastSurfaceIsClear(segment, cancellationToken));
        var coast = safeCoast ? DisplaySailPath(start, baseline, null) : null;
        var coastFlybys = safeCoast ? Flybys(baseline) : [];
        var coastWarning = safeCoast ? null : "The feathered coast intersects a body or passes too close to its surface.";
        if (trim is null) return new(coast, null, coastFlybys, [], coastWarning);

        var after = TraceCourseCoast(OrbitalNavigation.ToSystem(catalogue, ship.Primary, trim.EndTicks,
            trim.EndCoast.InitialState), ship.Primary, trim.EndTicks, through, cancellationToken);
        return new(coast, DisplaySailPath(start, after, trim, ship.Primary), coastFlybys, Flybys(after), coastWarning);
    }

    public void StartSailTrim(string id, double angleDegrees, int durationSeconds)
    {
        var ship = spacecraft[id];
        var path = CreateTrim(ship, angleDegrees, durationSeconds);
        ship.Sailing = path;
        ship.Coast = null;
        ship.AutomaticPrimary = false;
        ship.Generation++;
        Schedule(id, ship);
        CompactQueue();
    }

    public void FeatherSail(string id)
    {
        var ship = spacecraft[id];
        var path = ship.Sailing ?? throw new InvalidOperationException("The sail is already feathered.");
        var coast = new TwoBodyTrajectory(path.StateAt(ElapsedTicks), ElapsedTicks, PrimaryMu(ship.Primary));
        EndSailing(ship, coast);
        CompactQueue();
    }

    private SailTrajectory CreateTrim(Spacecraft ship, double angleDegrees, int durationSeconds, CancellationToken cancellationToken = default)
    {
        var sail = RequireSail(ship);
        if (ship.Coast is not { } coast) throw new InvalidOperationException("Feather the sail before commanding another trim.");
        var order = new ActiveSailState(ElapsedTicks, durationSeconds, angleDegrees,
            KinematicState.From(coast.StateAt(ElapsedTicks)));
        return BuildSailPath(ship.Primary, sail, order, cancellationToken);
    }

    private SailTrajectory BuildSailPath(string primary, SolarSail sail, ActiveSailState order, CancellationToken cancellationToken = default) =>
        new(catalogue, primary, sail, order, inspect => PathIsClear(inspect, primary, cancellationToken));

    private static SolarSail RequireSail(Spacecraft ship) => ship.Propulsion as SolarSail
        ?? throw new InvalidOperationException("This spacecraft has no solar sail.");

    private static void EndSailing(Spacecraft ship, TwoBodyTrajectory coast)
    {
        ship.Sailing = null;
        ship.Coast = coast;
        ship.AutomaticPrimary = true;
        ship.Generation++;
    }

    private SailFlightPath DisplaySailPath(long start, TrajectorySegment[] segments, SailTrajectory? trim, string? primary = null)
    {
        var points = new List<TrajectoryDisplayPoint>();
        if (trim is not null)
            AddSamples(start, trim.EndTicks, ticks => OrbitalNavigation.ToSystem(catalogue, primary!, ticks, trim.StateAt(ticks)));
        foreach (var segment in segments) AddSamples(segment.StartTicks, segment.EndTicks, segment.StateAt);
        return new([.. points]);

        void AddSamples(long from, long to, Func<long, CelestialState> sample)
        {
            for (var i = points.Count == 0 ? 0 : 1; i <= 48; i++)
            {
                var at = from + (to - from) * i / 48;
                var state = sample(at);
                points.Add(new(TickString(at), state.PositionKm, state.VelocityKmPerSecond));
            }
        }
    }

    private SailFlyby[] Flybys(TrajectorySegment[] segments)
    {
        var flybys = new List<SailFlyby>();
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var body = catalogue.Bodies[segment.PrimaryBodyId];
            if (body.ParentId is null || segment.Coast.ApoapsisKm is not null) continue;
            var entryLocal = segment.Coast.InitialState;
            var exitLocal = segment.Coast.StateAt(segment.EndTicks);
            var entering = IsInbound(entryLocal);
            var exiting = !IsInbound(exitLocal);
            var closest = entering && exiting ? segment.Coast.PeriapsisKm
                : Vector3d.From((entering ? exitLocal : entryLocal).PositionKm).Length;
            double? change = i > 0 && i < segments.Length - 1 && entering && exiting
                ? SolarSpeed(segment.EndTicks) - SolarSpeed(segment.StartTicks) : null;
            flybys.Add(new(body.Id, body.Name, closest - body.RadiusKm, change));

            double SolarSpeed(long ticks) => Vector3d.From(OrbitalNavigation.ToLocal(catalogue, SolarSystem.Sun,
                ticks, segment.StateAt(ticks)).VelocityKmPerSecond).Length;
        }
        return [.. flybys];
    }

    private static bool IsInbound(CelestialState state) =>
        Vector3d.Dot(Vector3d.From(state.PositionKm), Vector3d.From(state.VelocityKmPerSecond)) < 0;

    private static string TickString(long ticks) => ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
