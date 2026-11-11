using System.Text.Json.Serialization;

namespace Quasar.Simulation;

public sealed record LabObject(string Id, string Name, string Kind, Position3 Position, string? ParentId,
    Velocity3? VelocityKmPerSecond = null, KeplerianOrbit? Orbit = null, double RadiusKm = 0);
public sealed record LabEvent(long Sequence,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long AtTicks, string Kind, string ObjectId, string Message)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArrivalLabel => Kind switch
    {
        "course-approach-complete" => "Velocity-matched approach complete",
        "course-orbit-inserted" => "Orbital insertion complete",
        "arrival" => "Legacy body-center arrival",
        _ => null
    };

    internal string? ArrivalKind => Kind switch
    {
        "course-approach-complete" => "approach",
        "course-orbit-inserted" => "orbit",
        "arrival" => "body-center",
        _ => null
    };
}
public sealed record ShipStatus(string Status, double SpeedKmPerSecond, string? DestinationId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? ArrivalTicks, double RemainingDistanceKm,
    PropulsionPerformance? Propulsion = null,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? BurnEndTicks = null,
    string? ArrivalKind = null);
public sealed record LabSnapshot(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long ElapsedTicks, LabObject[] Objects, ShipStatus Ship, LabEvent[] Events,
    TrajectoryDisplay? Trajectory = null, string? ReferenceEpoch = null);
public sealed record TrajectoryDisplayPoint(string AtTicks, Position3 PositionKm, Velocity3 VelocityKmPerSecond);
public sealed record TrajectoryDisplay(string DepartureTicks, string ArrivalTicks, double TravelSeconds,
    double RequiredDeltaVKmPerSecond, TransferFuelEstimate Fuel, TrajectoryDisplayPoint[] Points, FlybyDisplay? Flyby = null,
    string? ArrivalKind = null, CourseArrival? Arrival = null, CourseBurnDisplay[]? BurnSchedule = null,
    double? BrakingMarginKg = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? BrakingMarginPercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CourseGravityTransition[]? GravityTransitions = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CriticalBrakingReserve => BrakingMarginPercent is < 1;
}
public sealed record CourseBurnDisplay(string Phase, string StartTicks, double DurationSeconds, double PropellantKg);
public sealed record CourseGravityTransition(string BodyId, string BodyName, string AtTicks);
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
    public bool HasModeledPropulsion => movement.PropulsionOf(ShipId) is not null;
    public string? CourseDestinationName => movement.CommittedPlan(ShipId)?.TargetBodyId is { } id
        ? celestialPositions.Bodies[id].Name : null;

    public SystemLab() : this(ExplorationTorch()) { }

    private SystemLab(SpacecraftPropulsion propulsion) : this(new MovementExecution(SolarSystem.CreateCatalogue()))
    {
        var earth = celestialPositions.GetState(SolarSystem.Earth, 0);
        var radial = Vector3d.From(earth.PositionKm);
        var position = (radial + radial / radial.Length * 1_000_000).Position;
        movement.AddSpacecraft(ShipId, new(position, earth.VelocityKmPerSecond), PrimaryId,
            propulsion, automaticPrimary: true);
        Record("created", "Solar System initialized. Pathfinder coasting near Earth with its starting propulsion loadout.");
    }

    internal static SystemLab CreateChemicalFixture() => new(new SpacecraftPropulsion(8000,
        new(1000, 3, 1.5, 200_000, 3000, "test-chemical"), new(1000, 10_000, 10_000, "test-chemical")));

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

    internal SystemLab(MovementExecution movement)
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
        var distance = ship.DestinationId is null ? 0 : ship.State.PositionKm.DistanceTo(display?.Arrival is not null
            ? display.Points[^1].PositionKm : states[ship.DestinationId].PositionKm);
        return new(ElapsedTicks,
            [.. bodies, new(ShipId, "Pathfinder", "spacecraft", ship.State.PositionKm, null, ship.State.VelocityKmPerSecond)],
            new(ship.Status, Vector3d.From(ship.State.VelocityKmPerSecond).Length, ship.DestinationId,
                ship.ArrivalTicks, distance, ship.Propulsion, ship.BurnEndTicks,
                display?.ArrivalKind ?? LastEvent.ArrivalKind), [.. events],
            display is null ? null : display with { Points = [.. display.Points], BurnSchedule = display.BurnSchedule?.ToArray() },
            PrimaryId == SolarSystem.Sun ? SolarSystem.Epoch : null);
    }

    public TrajectoryPlan PreviewTravel(string destinationId, int flightSeconds = 21600, double? maneuverBudget = null)
    {
        if (flightSeconds is < 1 or > CourseSearch.MaximumSeconds)
            throw new ArgumentException("Flight time must be between one second and ten Julian years.");
        var (state, departure, leg) = CaptureDeparture(ShipId, destinationId);
        leg = leg with { ArrivalTicks = checked(departure + (long)flightSeconds * TimeSpan.TicksPerSecond) };
        return maneuverBudget is { } budget && leg.PrimaryBodyId == SolarSystem.Sun
            ? planner.PlanWithFlyby(state, departure, leg, budget)
            : planner.Plan(new(state, departure, leg, maneuverBudget));
    }

    private (CelestialState State, long Departure, TransferLeg Leg) CaptureDeparture(string shipId, string destinationId,
        bool automatic = false)
    {
        if (!celestialPositions.Bodies.TryGetValue(destinationId, out var destination)
            || (!automatic && destination.Kind is not (CelestialBodyKind.Planet or CelestialBodyKind.Moon)))
            throw new ArgumentException("Choose a destination in this system.", nameof(destinationId));
        if (movement.IsBurning(shipId)) throw new InvalidOperationException("Cut thrust before plotting a course.");
        var departure = ElapsedTicks;
        var state = movement.StateAt(shipId, departure);
        if (state.PositionKm.DistanceTo(celestialPositions.GetState(destinationId, departure).PositionKm) < 1e-6)
            throw new InvalidOperationException("Already at that destination.");
        var primary = PrimaryId;
        return (state, departure, new(primary, departure, new(destinationId),
            MinimumPrimaryClearanceKm: Math.Max(1, celestialPositions.Bodies[primary].RadiusKm)));
    }

    public void OrderTravel(string destinationId, int flightSeconds = 21600)
    {
        if (movement.CommittedPlan(ShipId)?.TargetBodyId == destinationId)
            throw new InvalidOperationException("Already travelling to that destination.");
        var plan = PreviewTravel(destinationId, flightSeconds);
        CommitTravel(plan);
    }

    public void CommitTravel(TrajectoryPlan plan)
    {
        var destinationId = plan.TargetBodyId;
        if (destinationId is null || !celestialPositions.Bodies.ContainsKey(destinationId))
            throw new ArgumentException("Choose a celestial destination in this system.");
        var presentation = Present(plan);
        movement.Commit(ShipId, plan); display = presentation;
        var name = celestialPositions.Bodies[destinationId].Name;
        Record("order", plan.Finite is null ? $"Pathfinder transfer committed to {name}; departure impulse applied."
            : $"Pathfinder began the planned departure burn toward {name}.");
    }

    public void CancelTravel()
    {
        movement.Cancel(ShipId); display = null;
        Record("cancel", "Pathfinder course cancelled; retaining velocity on an unforced gravitational coast.");
    }

    public void StartBurn(string direction, int durationSeconds)
    {
        movement.StartBurn(ShipId, direction, durationSeconds);
        Record("burn-start", $"Pathfinder ignited for a {direction} burn.");
    }

    public void CutThrust()
    {
        movement.CutThrust(ShipId);
        Record("burn-cutoff", "Pathfinder cut thrust and resumed an unforced coast.");
    }

    internal static SpacecraftPropulsion ExplorationTorch() => new(8000,
        new(1000, 3, 1.5, 300_000, 1_000_000, "torch-test"),
        new(1000, 20_000, 20_000, "torch-test"));

    public void FitExplorationTorch()
    {
        movement.RefitCoastingSpacecraft(ShipId, ExplorationTorch());
        Record("refit", "Pathfinder fitted the exploration torch and loaded 20,000 kg of reaction mass.");
    }

    public LabEvent LastEvent => events[^1];
    public string LastEventKind => LastEvent.Kind;

    public OrbitalTargetState TargetStateAt(string kind, string targetId, long atTicks) => kind switch
    {
        "orbit" => ResolvePresetOrbit(targetId, atTicks),
        "station" => OrbitalNavigation.ResolveStation(celestialPositions, targetId, atTicks),
        "spacecraft" => movement.ResolveSpacecraftTarget(targetId, atTicks),
        _ => throw new ArgumentException("Choose orbit, station, or spacecraft.", nameof(kind))
    };

    private OrbitalTargetState ResolvePresetOrbit(string bodyId, long atTicks)
    {
        if (!celestialPositions.Bodies.TryGetValue(bodyId, out var body) || body.GravitationalParameterKm3PerSecond2 <= 0)
            throw new ArgumentException("Choose a destination with modeled gravity for orbital insertion.", nameof(bodyId));
        var altitude = bodyId switch
        {
            SolarSystem.Earth => 400,
            "moon-moon" => 100,
            _ => Math.Max(100, body.RadiusKm * 0.1)
        };
        var radius = body.RadiusKm + altitude;
        if (celestialPositions.GetEnvironment(bodyId).SphereOfInfluenceRadiusKm is { } sphere)
        {
            var surface = Math.Max(1, body.RadiusKm);
            if (sphere <= surface)
                throw new ArgumentException("No above-surface orbit fits inside this destination's modeled gravity sphere. Choose a velocity-matched approach.");
            radius = Math.Min(radius, (surface + sphere) / 2);
        }
        var speed = Math.Sqrt(body.GravitationalParameterKm3PerSecond2 / radius);
        return OrbitalNavigation.ResolveOrbit(celestialPositions, bodyId, 0, new(new(radius, 0, 0), new(0, speed, 0)), atTicks);
    }

    public CelestialState ShipStateAt(long atTicks) => movement.StateAt(ShipId, atTicks);

    public static TrajectoryDisplay Present(TrajectoryPlan plan) => new(Ticks(plan.DepartureTicks), Ticks(plan.ArrivalTicks),
        plan.TravelSeconds, plan.RequiredDeltaVKmPerSecond, plan.Fuel,
        plan.Sample().Select(p => new TrajectoryDisplayPoint(Ticks(p.AtTicks), p.State.PositionKm, p.State.VelocityKmPerSecond)).ToArray(),
        plan.Flyby is { } flyby ? new(flyby.BodyId, plan.Catalogue.Bodies[flyby.BodyId].Name,
            Ticks(plan.Segments[0].EndTicks), flyby.ClosestApproachKm) : null,
        plan.Finite?.Arrival.Kind ?? (plan.Destination.BodyId is not null ? "body-center" : "waypoint"),
        plan.Finite?.Arrival, plan.Finite is { } finite ?
        [new("Departure", Ticks(finite.Departure.StartTicks), Seconds(finite.Departure),
            finite.State.Propulsion.Tank.QuantityKg - finite.Departure.RemainingPropellantKg(finite.Departure.EndTicks)),
         new("Braking", Ticks(finite.Braking.StartTicks), Seconds(finite.Braking), finite.BrakingPropellantKg)] : null,
        plan.Finite?.RemainingPropellantKg, plan.Finite is { } solved
            ? solved.RemainingPropellantKg / solved.State.Propulsion.Tank.QuantityKg * 100 : null,
        plan.Finite is { } course && course.GravityTransitionTicks.Length > 0
            ? course.GravityTransitionTicks.Select(ticks => new CourseGravityTransition(course.PrimaryAt(ticks),
                plan.Catalogue.Bodies[course.PrimaryAt(ticks)].Name, Ticks(ticks))).ToArray() : null);

    private static double Seconds(CourseBurn burn) => (burn.EndTicks - burn.StartTicks) / (double)TimeSpan.TicksPerSecond;

    public bool Advance(TimeSpan duration)
    {
        var plan = movement.CommittedPlan(ShipId);
        var flyby = plan?.Flyby;
        var consequences = movement.Advance(duration);
        foreach (var consequence in consequences)
        {
            if (consequence.Kind is "arrival" or "course-approach-complete" or "course-orbit-inserted") display = null;
            Record(consequence.Kind, consequence.Kind switch
            {
                "burn-complete" => "Pathfinder completed its timed burn and resumed an unforced coast.",
                "burn-exhausted" => "Pathfinder exhausted its propellant and resumed an unforced coast.",
                "arrival" => "Body-center arrival. Pathfinder applied the planned velocity-matching impulse.",
                "course-departure-cutoff" => "Pathfinder completed the departure burn and began the transfer coast.",
                "course-braking-start" => "Pathfinder began the planned braking burn.",
                "course-orbit-inserted" => "Pathfinder completed braking and entered the requested orbit.",
                "course-approach-complete" => "Pathfinder completed braking at the velocity-matched approach point and resumed a gravitational coast.",
                "primary-change" => $"Pathfinder changed gravity primary to {celestialPositions.Bodies[movement.PrimaryOf(ShipId)].Name} without thrust.",
                _ => flyby is null ? "Pathfinder changed gravitational primary at a committed patch boundary."
                    : $"Pathfinder completed a gravity assist at {celestialPositions.Bodies[flyby.BodyId].Name}."
            });
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
