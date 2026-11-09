using System.Text.Json.Serialization;

namespace Quasar.Simulation;

public sealed record LabObject(string Id, string Name, string Kind, Position3 Position, string? ParentId,
    Velocity3? VelocityKmPerSecond = null, KeplerianOrbit? Orbit = null, double RadiusKm = 0);
public sealed record LabEvent(long Sequence,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long AtTicks, string Kind, string ObjectId, string Message);
public sealed record ShipStatus(string Status, double SpeedKmPerSecond, string? DestinationId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? ArrivalTicks, double RemainingDistanceKm);
public sealed record LabSnapshot(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long ElapsedTicks, LabObject[] Objects, ShipStatus Ship, LabEvent[] Events,
    TrajectoryDisplay? Trajectory = null, string? ReferenceEpoch = null);
public sealed record TrajectoryDisplayPoint(string AtTicks, Position3 PositionKm, Velocity3 VelocityKmPerSecond);
public sealed record TrajectoryDisplay(string DepartureTicks, string ArrivalTicks, double TravelSeconds,
    double RequiredDeltaVKmPerSecond, TransferFuelEstimate Fuel, TrajectoryDisplayPoint[] Points, FlybyDisplay? Flyby = null);
public sealed record FlybyDisplay(string BodyId, string BodyName, string AtTicks, double ClosestApproachKm);

/// <summary>Headless fixture using the planner/execution contracts available to future fleet systems.</summary>
public sealed partial class SystemLab
{
    public const string ShipId = "ship-pathfinder";
    private const string LegacyPrimaryId = "star-helios";
    private string PrimaryId => celestialPositions.Bodies.ContainsKey(SolarSystem.Sun) ? SolarSystem.Sun : LegacyPrimaryId;
    private static CelestialPositionService CreateLegacyCatalogue() => new(
    [
        new(LegacyPrimaryId, "Helios", CelestialBodyKind.Star, 1.32712440018e11),
        new("planet-iris", "Iris", CelestialBodyKind.Planet, 398600.435, LegacyPrimaryId,
            ReferenceState: new(new(1_200_000, 400_000, 200_000), default)),
        new("planet-nereid", "Nereid", CelestialBodyKind.Planet, 42828.375, LegacyPrimaryId,
            ReferenceState: new(new(-2_000_000, 1_300_000, -600_000), default))
    ]);
    private readonly CelestialPositionService celestialPositions;
    private readonly MovementExecution movement;
    private readonly TrajectoryPlanner planner;
    private readonly List<LabEvent> events = [];
    private TrajectoryDisplay? display;
    public ICelestialPositionService CelestialPositions => celestialPositions;
    public long ElapsedTicks => movement.ElapsedTicks;

    public SystemLab() : this(new MovementExecution(SolarSystem.CreateCatalogue()))
    {
        var earth = celestialPositions.GetState(SolarSystem.Earth, 0);
        var radial = Vector3d.From(earth.PositionKm);
        var position = (radial + radial / radial.Length * 1_000_000).Position;
        movement.AddSpacecraft(ShipId, new(position, earth.VelocityKmPerSecond), PrimaryId);
        Record("created", "Solar System initialized. Pathfinder coasting near Earth.");
    }

    internal static SystemLab CreateLegacyFixture()
    {
        var lab = new SystemLab(new MovementExecution(CreateLegacyCatalogue()));
        var position = new Position3(900_000, -200_000, 350_000);
        var r = Vector3d.From(position);
        var transverse = Vector3d.Cross(new(0, 0, 1), r);
        var speed = Math.Sqrt(lab.celestialPositions.GetEnvironment(LegacyPrimaryId).GravitationalParameterKm3PerSecond2 / r.Length);
        lab.movement.AddSpacecraft(ShipId, new(position, (transverse / transverse.Length * speed).Velocity), LegacyPrimaryId);
        lab.Record("created", "System Lab initialized. Pathfinder coasting in Helios gravity.");
        return lab;
    }

    private SystemLab(MovementExecution movement)
    {
        this.movement = movement;
        celestialPositions = movement.Catalogue;
        planner = new(celestialPositions);
    }

    public LabSnapshot Snapshot()
    {
        var states = celestialPositions.GetStates(ElapsedTicks);
        var bodies = celestialPositions.Bodies.Values.Select(body => new LabObject(body.Id, body.Name,
            body.Kind.ToString().ToLowerInvariant(), states[body.Id].PositionKm, body.ParentId,
            states[body.Id].VelocityKmPerSecond, body.Orbit, body.RadiusKm)).OrderBy(body => body.ParentId is null ? 0 : 1)
            .ThenBy(body => body.Id, StringComparer.Ordinal).ToArray();
        var ship = movement.View(ShipId);
        if (display is null && movement.CommittedPlan(ShipId) is { } plan) display = Present(plan);
        var distance = ship.DestinationId is null ? 0 : ship.State.PositionKm.DistanceTo(states[ship.DestinationId].PositionKm);
        return new(ElapsedTicks,
            [.. bodies, new(ShipId, "Pathfinder", "spacecraft", ship.State.PositionKm, null, ship.State.VelocityKmPerSecond)],
            new(ship.Status, Vector3d.From(ship.State.VelocityKmPerSecond).Length, ship.DestinationId,
                ship.ArrivalTicks, distance), [.. events], display is null ? null : display with { Points = [.. display.Points] },
            PrimaryId == SolarSystem.Sun ? SolarSystem.Epoch : null);
    }

    public TrajectoryPlan PreviewTravel(string destinationId, int flightSeconds = 21600, double? maneuverBudget = null)
    {
        if (!celestialPositions.Bodies.TryGetValue(destinationId, out var destination) || destination.Kind != CelestialBodyKind.Planet)
            throw new ArgumentException("Choose a planet in this system.", nameof(destinationId));
        if (flightSeconds is < 1 or > 315576000) throw new ArgumentException("Flight time must be between one second and ten Julian years.");
        var state = movement.StateAt(ShipId, ElapsedTicks);
        if (state.PositionKm.DistanceTo(celestialPositions.GetState(destinationId, ElapsedTicks).PositionKm) < 1e-6)
            throw new InvalidOperationException("Already at that planet.");
        var leg = new TransferLeg(PrimaryId,
            checked(ElapsedTicks + (long)flightSeconds * TimeSpan.TicksPerSecond), new(destinationId),
            MinimumPrimaryClearanceKm: Math.Max(1, celestialPositions.Bodies[PrimaryId].RadiusKm));
        return maneuverBudget is { } budget && PrimaryId == SolarSystem.Sun
            ? planner.PlanWithFlyby(state, ElapsedTicks, leg, budget)
            : planner.Plan(new(state, ElapsedTicks, leg, maneuverBudget));
    }

    public void OrderTravel(string destinationId, int flightSeconds = 21600)
    {
        if (movement.CommittedPlan(ShipId)?.Destination.BodyId == destinationId)
            throw new InvalidOperationException("Already travelling to that planet.");
        var plan = PreviewTravel(destinationId, flightSeconds);
        CommitTravel(plan);
    }

    public void CommitTravel(TrajectoryPlan plan)
    {
        var destinationId = plan.Destination.BodyId;
        if (destinationId is null || !celestialPositions.Bodies.ContainsKey(destinationId))
            throw new ArgumentException("Choose a celestial destination in this system.");
        var presentation = Present(plan);
        movement.Commit(ShipId, plan); display = presentation;
        Record("order", $"Pathfinder transfer committed to {celestialPositions.Bodies[destinationId].Name}; departure impulse applied.");
    }

    public void CancelTravel()
    {
        movement.Cancel(ShipId); display = null;
        Record("cancel", "Pathfinder course cancelled; retaining velocity on an unforced gravitational coast.");
    }

    public CelestialState ShipStateAt(long atTicks) => movement.StateAt(ShipId, atTicks);

    public static TrajectoryDisplay Present(TrajectoryPlan plan) => new(Ticks(plan.DepartureTicks), Ticks(plan.ArrivalTicks),
        plan.TravelSeconds, plan.RequiredDeltaVKmPerSecond, plan.Fuel,
        plan.Sample().Select(p => new TrajectoryDisplayPoint(Ticks(p.AtTicks), p.State.PositionKm, p.State.VelocityKmPerSecond)).ToArray(),
        plan.Flyby is { } flyby ? new(flyby.BodyId, plan.Catalogue.Bodies[flyby.BodyId].Name,
            Ticks(plan.Segments[0].EndTicks), flyby.ClosestApproachKm) : null);

    public bool Advance(TimeSpan duration)
    {
        var flyby = movement.CommittedPlan(ShipId)?.Flyby;
        var consequences = movement.Advance(duration);
        foreach (var consequence in consequences)
        {
            if (consequence.Kind == "arrival") display = null;
            Record(consequence.Kind, consequence.Kind == "arrival"
                ? "Pathfinder arrived. Arrival impulse applied; awaiting orders."
                : flyby is null ? "Pathfinder changed gravitational primary at a committed patch boundary."
                    : $"Pathfinder completed a gravity assist at {celestialPositions.Bodies[flyby.BodyId].Name}.");
        }
        return consequences.Length > 0;
    }

    private static string Ticks(long ticks) => ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private void Record(string kind, string message)
    {
        lastEventSequence = checked(lastEventSequence + 1);
        events.Add(new(lastEventSequence, ElapsedTicks, kind, ShipId, message));
    }
}
