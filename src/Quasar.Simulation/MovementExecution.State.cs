using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    internal CelestialPositionService Catalogue => catalogue;

    /// <summary>Capture on the simulation writer. The result can be serialized after releasing the writer.</summary>
    public SimulationState ExportState() => new(SimulationState.CurrentFormatVersion, ElapsedTicks,
        [.. catalogue.Bodies.Values.OrderBy(body => body.Id, StringComparer.Ordinal).Select(ExportBody)],
        [.. spacecraft.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => ExportShip(pair.Key, pair.Value))]);

    /// <summary>Build a new execution owner or throw InvalidDataException. No existing world is involved.</summary>
    public static MovementExecution RestoreState(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.RequireSupportedVersion();
        Require(!state.Bodies.IsDefault && !state.Spacecraft.IsDefault, "Missing catalogue or spacecraft.");
        try
        {
            var catalogue = new CelestialPositionService(state.Bodies.Select(RestoreBody));
            catalogue.GetStates(state.ElapsedTicks);
            var restored = new MovementExecution(catalogue) { ElapsedTicks = state.ElapsedTicks };
            foreach (var ship in state.Spacecraft)
                restored.RestoreShip(ship);
            restored.ScheduleRestoredBoundaries();
            return restored;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or ArithmeticException or KeyNotFoundException)
        {
            throw new InvalidDataException("Invalid catalogue or spacecraft physics in simulation state.", exception);
        }
    }

    private void ScheduleRestoredBoundaries()
    {
        // Enqueue only the next live boundary per ship, in a stable order independent of payload order.
        foreach (var (id, ship) in spacecraft.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Schedule(id, ship);
    }

    private static CelestialBodyState ExportBody(CelestialBody body) => new(body.Id, body.Name, body.Kind,
        body.GravitationalParameterKm3PerSecond2, body.ParentId,
        body.Orbit is { } orbit ? new(orbit.SemiMajorAxisKm, orbit.Eccentricity, orbit.InclinationRadians,
            orbit.LongitudeOfAscendingNodeRadians, orbit.ArgumentOfPeriapsisRadians, orbit.MeanAnomalyAtEpochRadians,
            orbit.EpochTicks) : null, KinematicState.From(body.ReferenceState), body.ReferenceEpochTicks, body.RadiusKm);

    private static CelestialBody RestoreBody(CelestialBodyState body)
    {
        Require(body is not null && body.ReferenceState is not null, "Missing celestial body or reference state.");
        return new(body.Id, body.Name, body.Kind, body.GravitationalParameterKm3PerSecond2, body.ParentId,
            body.Orbit is { } orbit ? new(orbit.SemiMajorAxisKm, orbit.Eccentricity, orbit.InclinationRadians,
                orbit.LongitudeOfAscendingNodeRadians, orbit.ArgumentOfPeriapsisRadians, orbit.MeanAnomalyAtEpochRadians,
                orbit.EpochTicks) : null, body.ReferenceState.ToRuntime(), body.ReferenceEpochTicks, body.RadiusKm);
    }

    private SpacecraftState ExportShip(string id, Spacecraft ship) => new(id, ship.Primary,
        ship.Coast is { } coast ? ExportCoast(coast) with { AutomaticPrimary = ship.AutomaticPrimary } : null,
        ship.Plan is { Finite: null } plan ? ExportPlan(plan, ship.NextBoundary) : null,
        ship.Rendezvous is { } rendezvous ? new(ExportDestination(rendezvous.Destination), rendezvous.ArrivalTicks) : null,
        PropulsionAt(ship, ElapsedTicks), ship.Burn?.State, ship.Plan?.Finite?.State,
        ship.Propulsion as SolarSail, ship.Sailing?.Order);

    private static CoastState ExportCoast(TwoBodyTrajectory coast) =>
        new(coast.EpochTicks, KinematicState.From(coast.InitialState));

    private static DestinationState ExportDestination(TrajectoryDestination destination) =>
        new(destination.BodyId, KinematicState.From(destination.ReferenceState), destination.EpochTicks);

    private static CommittedTrajectoryState ExportPlan(TrajectoryPlan plan, int nextBoundary) => new(
        KinematicState.From(plan.InitialState), ExportDestination(plan.Destination),
        [.. plan.Segments.Select(segment => new TrajectorySegmentState(segment.PrimaryBodyId,
            ExportCoast(segment.Coast), segment.EndTicks))],
        [.. plan.Burns.Select(burn => new TrajectoryBurnState(burn.AtTicks, burn.Phase,
            CartesianState.From(burn.DeltaVelocityKmPerSecond)))],
        new(plan.Fuel.PropellantMassKg, plan.Fuel.UnavailableReason), nextBoundary, plan.Flyby);

    private void RestoreShip(SpacecraftState state)
    {
        Require(state is not null && !string.IsNullOrWhiteSpace(state.Id), "Missing spacecraft identity.");
        PrimaryMu(state.PrimaryBodyId);
        var component = (IPropulsionComponent?)state.Propulsion ?? state.Sail;
        component?.Validate();
        if (state.Sail is not null)
        {
            Require(state is { Propulsion: null, ActiveBurn: null, Trajectory: null, Rendezvous: null, FiniteCourse: null }
                && catalogue.Bodies.TryGetValue(SolarSystem.Sun, out var sun) && sun.Kind == CelestialBodyKind.Star,
                "A solar sail requires sunlight and cannot share the rocket's propulsion or motion state.");
        }
        var ship = new Spacecraft(state.PrimaryBodyId, null, component);
        Require(state.ActiveSail is null || state is { Sail: not null, FreeCoast: null },
            "Active sail trim requires a solar sail and must be the sole motion state.");
        if (state.FiniteCourse is { } finite)
        {
            Require(state is { FreeCoast: null, Trajectory: null, Rendezvous: null, ActiveBurn: null, Propulsion: not null },
                "Finite course must be the sole motion state.");
            var course = FiniteCourse.Build(catalogue, finite);
            Require(finite.DepartureTicks <= ElapsedTicks && ElapsedTicks < finite.ArrivalTicks
                && state.Propulsion == WithPropellant(finite.Propulsion, course.PropellantAt(ElapsedTicks))
                && course.PrimaryAt(ElapsedTicks) == state.PrimaryBodyId
                && !CourseCrossesPrimaryBoundary(course, CancellationToken.None), "Finite course disagrees with clock, fuel, or primary.");
            ship.Plan = course.ToPlan();
            ship.NextBoundary = course.Boundaries.Count(boundary => boundary.Ticks <= ElapsedTicks);
            Require(spacecraft.TryAdd(state.Id, ship), "Duplicate spacecraft identity.");
            StateAt(state.Id, ElapsedTicks).RequireFinite();
            return;
        }
        switch (state)
        {
            case { FreeCoast: { } coast, Trajectory: null, Rendezvous: null, ActiveBurn: null }:
                Require(coast.EpochTicks <= ElapsedTicks, "Free coast starts after the world clock.");
                Require(state.Sail is null || coast.AutomaticPrimary, "A coasting sailcraft requires automatic gravity transitions.");
                ship.Coast = RestoreCoast(coast, state.PrimaryBodyId);
                ship.AutomaticPrimary = coast.AutomaticPrimary;
                break;
            case { FreeCoast: null, Trajectory: { } trajectory, Rendezvous: null, ActiveBurn: null }:
                ship.Plan = RestorePlan(trajectory);
                ship.NextBoundary = trajectory.NextBoundaryIndex;
                var current = ship.Plan.Segments[ship.NextBoundary];
                Require(current.StartTicks <= ElapsedTicks && ElapsedTicks < current.EndTicks
                    && current.PrimaryBodyId == state.PrimaryBodyId, "Active segment disagrees with clock or primary.");
                break;
            case { FreeCoast: null, Trajectory: null, Rendezvous: { } rendezvous, ActiveBurn: null }:
                Require(rendezvous.ArrivalTicks <= ElapsedTicks, "Rendezvous arrives after the world clock.");
                var destination = RestoreDestination(rendezvous.Destination);
                destination.StateAt(catalogue, rendezvous.ArrivalTicks);
                ship.Rendezvous = (destination, rendezvous.ArrivalTicks);
                break;
            case { FreeCoast: null, Trajectory: null, Rendezvous: null, ActiveBurn: { } burn, Propulsion: { } propulsion }:
                Require(burn.StartTicks <= ElapsedTicks && burn.InitialRelativeState is not null && burn.UnitDirection is not null,
                    "Incomplete active burn or ignition epoch after the clock.");
                var ignitionPropulsion = WithPropellant(propulsion, burn.IgnitionPropellantKg);
                ship.Propulsion = ignitionPropulsion;
                ship.Burn = BuildBurn(ship.Primary, ignitionPropulsion, burn);
                Require(ElapsedTicks < ship.Burn.Path.EndTicks
                    && ship.Burn.Path.RemainingPropellantKg(ElapsedTicks) == propulsion.Tank.QuantityKg,
                    "Active burn disagrees with clock or propellant quantity.");
                break;
            case { ActiveSail: { } trim, Sail: { } sailComponent }:
                Require(trim.StartTicks <= ElapsedTicks && trim.InitialRelativeState is not null, "Invalid sail trim epoch or initial state.");
                ship.Sailing = BuildSailPath(ship.Primary, sailComponent, trim);
                Require(ElapsedTicks < ship.Sailing.EndTicks, "Active sail trim disagrees with the clock.");
                break;
            default:
                throw new InvalidDataException("A spacecraft must have exactly one motion state.");
        }
        Require(spacecraft.TryAdd(state.Id, ship), "Duplicate spacecraft identity.");
        StateAt(state.Id, ElapsedTicks).RequireFinite();
    }

    private double PrimaryMu(string id)
    {
        if (id is null || !catalogue.Bodies.TryGetValue(id, out var body) || body.GravitationalParameterKm3PerSecond2 <= 0)
            throw new InvalidDataException("Unknown or non-gravitating primary.");
        return body.GravitationalParameterKm3PerSecond2;
    }

    private TwoBodyTrajectory RestoreCoast(CoastState coast, string primaryId)
    {
        Require(coast is not null && coast.InitialRelativeState is not null, "Missing coast or initial relative state.");
        return new(coast.InitialRelativeState.ToRuntime(), coast.EpochTicks, PrimaryMu(primaryId));
    }

    private TrajectoryDestination RestoreDestination(DestinationState destination)
    {
        Require(destination is not null && destination.ReferenceState is not null, "Missing trajectory destination.");
        Require(destination.BodyId is null || catalogue.Bodies.ContainsKey(destination.BodyId), "Unknown destination body.");
        return new(destination.BodyId, destination.ReferenceState.ToRuntime(), destination.EpochTicks);
    }

    private TrajectoryPlan RestorePlan(CommittedTrajectoryState state)
    {
        Require(state.InitialState is not null && !state.Segments.IsDefault && state.Segments.Length is >= 1 and <= 32
            && !state.Burns.IsDefault && state.Burns.Length == state.Segments.Length + 1, "Incomplete solved trajectory.");
        Require(state.NextBoundaryIndex >= 0 && state.NextBoundaryIndex < state.Segments.Length, "Invalid next boundary.");
        Require(state.Fuel is not null && (state.Fuel.PropellantMassKg is null
            || (double.IsFinite(state.Fuel.PropellantMassKg.Value) && state.Fuel.PropellantMassKg.Value >= 0)),
            "Missing or invalid fuel estimate.");
        var initial = state.InitialState.ToRuntime();
        var destination = RestoreDestination(state.Destination);
        var segments = new TrajectorySegment[state.Segments.Length];
        var burns = new TrajectoryBurn[state.Burns.Length];
        var before = initial;
        for (var i = 0; i < segments.Length; i++)
        {
            var saved = state.Segments[i];
            Require(saved is not null && saved.Coast is { AutomaticPrimary: false }, "Missing or invalid trajectory segment.");
            var coast = RestoreCoast(saved.Coast, saved.PrimaryBodyId);
            var start = coast.EpochTicks;
            Require(saved.EndTicks > start && (decimal)saved.EndTicks - start <= TimeSpan.TicksPerDay * 36525m
                && (i == 0 || segments[i - 1].EndTicks == start), "Invalid or noncontiguous segment epochs.");
            if (i > 0 && segments[i - 1].PrimaryBodyId != saved.PrimaryBodyId)
                TrajectoryPlanner.ValidatePatch(catalogue, segments[i - 1].PrimaryBodyId, saved.PrimaryBodyId,
                    before.PositionKm, start);
            var localBefore = TrajectoryPlanner.Subtract(before, catalogue.GetState(saved.PrimaryBodyId, start));
            RequireNearPosition(localBefore.PositionKm, localBefore.PositionKm.DistanceTo(coast.InitialState.PositionKm),
                "Discontinuous trajectory start.");
            var impulse = Vector3d.From(coast.InitialState.VelocityKmPerSecond) - Vector3d.From(localBefore.VelocityKmPerSecond);
            burns[i] = RestoreBurn(state.Burns[i], start, i == 0 ? "departure" : "patch", impulse);

            var segment = new TrajectorySegment(catalogue, saved.PrimaryBodyId, coast, saved.EndTicks);
            before = segment.StateAt(saved.EndTicks);
            before.RequireFinite();
            segments[i] = segment;
        }
        var last = segments[^1];
        var target = destination.StateAt(catalogue, last.EndTicks);
        var primaryAtArrival = catalogue.GetState(last.PrimaryBodyId, last.EndTicks);
        RequireNearPosition(TrajectoryPlanner.Subtract(target, primaryAtArrival).PositionKm,
            target.PositionKm.DistanceTo(before.PositionKm), "Trajectory misses its destination.");
        burns[^1] = RestoreBurn(state.Burns[^1], last.EndTicks, "arrival",
            Vector3d.From(target.VelocityKmPerSecond) - Vector3d.From(before.VelocityKmPerSecond));
        Require(state.Flyby is null || TrajectoryPlanner.IsValidFlyby(catalogue, state.Flyby, segments),
            "Invalid saved gravity assist.");
        var plan = new TrajectoryPlan(catalogue, initial, destination, segments, burns,
            new(state.Fuel.PropellantMassKg, state.Fuel.UnavailableReason), state.Flyby);
        Require(double.IsFinite(plan.RequiredDeltaVKmPerSecond), "Nonfinite total delta-v.");
        return plan;
    }

    private static void RequireNearPosition(Position3 expected, double distance, string message)
    {
        var radius = Vector3d.From(expected).Length;
        Require(double.IsFinite(radius) && distance <= Math.Max(1e-5, radius * 1e-8), message);
    }

    private static TrajectoryBurn RestoreBurn(TrajectoryBurnState saved, long at, string phase, Vector3d expected)
    {
        Require(saved is not null && saved.AtTicks == at && saved.Phase == phase && saved.DeltaVelocityKmPerSecond is not null,
            "Missing or misplaced trajectory burn.");
        var velocity = saved.DeltaVelocityKmPerSecond.ToVelocity();
        var actual = Vector3d.From(velocity);
        Require(double.IsFinite(expected.Length) && double.IsFinite(actual.Length)
            && (actual - expected).Length <= Math.Max(1e-8, expected.Length * 1e-8), "Burn disagrees with solved velocity change.");
        return new(at, phase, velocity);
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
