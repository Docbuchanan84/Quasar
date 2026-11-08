namespace Quasar.Simulation;

public sealed record LabObject(string Id, string Name, string Kind, Position3 Position, string? ParentId,
    Velocity3? VelocityKmPerSecond = null);
public sealed record LabEvent(long Sequence, long AtTicks, string Kind, string ObjectId, string Message);
public sealed record TravelPlan(string DestinationId, Position3 DeparturePosition, long DepartureTicks, long ArrivalTicks);
public sealed record ShipStatus(string Status, double SpeedKmPerSecond, string? DestinationId,
    long? ArrivalTicks, double RemainingDistanceKm);
public sealed record LabSnapshot(long ElapsedTicks, LabObject[] Objects, ShipStatus Ship, LabEvent[] Events);

/// <summary>A deliberately small, headless fixture. No real-time timer, network, or storage dependency.</summary>
public sealed class SystemLab
{
    public const string ShipId = "ship-pathfinder";
    public const double ShipSpeed = 25;
    // Preserve the accepted fixed-body travel lab. Orbital fixtures use the same service;
    // moving-target interception and spacecraft trajectories require their own task.
    private readonly CelestialPositionService celestialPositions = new(
    [
        new("star-helios", "Helios", CelestialBodyKind.Star, 1.32712440018e11),
        new("planet-iris", "Iris", CelestialBodyKind.Planet, 398600.435, "star-helios",
            ReferenceState: new(new(1_200_000, 400_000, 200_000), default)),
        new("planet-nereid", "Nereid", CelestialBodyKind.Planet, 42828.375, "star-helios",
            ReferenceState: new(new(-2_000_000, 1_300_000, -600_000), default))
    ]);
    public ICelestialPositionService CelestialPositions => celestialPositions;
    private readonly List<LabEvent> events = [];
    private Position3 shipPosition = new(900_000, -200_000, 350_000);
    private TravelPlan? travel;
    public long ElapsedTicks { get; private set; }

    public SystemLab() => Record("created", ShipId, "System Lab initialized. Pathfinder standing by.");

    public LabSnapshot Snapshot()
    {
        var states = celestialPositions.GetStates(ElapsedTicks);
        var bodies = celestialPositions.Bodies.Values.Select(body => new LabObject(body.Id, body.Name,
            body.Kind.ToString().ToLowerInvariant(), states[body.Id].PositionKm, body.ParentId,
            states[body.Id].VelocityKmPerSecond)).OrderBy(body => body.ParentId is null ? 0 : 1)
            .ThenBy(body => body.Id, StringComparer.Ordinal).ToArray();
        var destination = travel is null ? null : bodies.Single(body => body.Id == travel.DestinationId);
        return new(ElapsedTicks,
            [.. bodies, new(ShipId, "Pathfinder", "spacecraft", shipPosition, null)],
            new(travel is null ? "Idle" : "Underway", ShipSpeed, travel?.DestinationId,
                travel?.ArrivalTicks, destination is null ? 0 : shipPosition.DistanceTo(destination.Position)),
            [.. events]);
    }

    public void OrderTravel(string destinationId)
    {
        var destination = celestialPositions.Bodies.Values.SingleOrDefault(body => body.Id == destinationId && body.Kind == CelestialBodyKind.Planet)
            ?? throw new ArgumentException("Choose a planet in this system.", nameof(destinationId));
        if (travel?.DestinationId == destinationId)
            throw new InvalidOperationException("Already travelling to that planet.");
        var distance = shipPosition.DistanceTo(celestialPositions.GetState(destinationId, ElapsedTicks).PositionKm);
        if (distance == 0) throw new InvalidOperationException("Already at that planet.");
        var durationTicks = checked((long)Math.Ceiling(distance / ShipSpeed * TimeSpan.TicksPerSecond));
        var arrival = checked(ElapsedTicks + durationTicks);
        travel = new(destinationId, shipPosition, ElapsedTicks, arrival);
        Record("order", ShipId, $"Pathfinder ordered to {destination.Name}.");
    }

    /// <summary>Advance directly to a deadline; arrival interrupts the requested interval.</summary>
    public bool Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(duration.Ticks);
        var targetTicks = checked(ElapsedTicks + duration.Ticks); // Validate before changing any state.
        if (travel is { } plan)
        {
            var destination = celestialPositions.Bodies[plan.DestinationId];
            var destinationPosition = celestialPositions.GetState(plan.DestinationId, plan.ArrivalTicks).PositionKm;
            if (plan.ArrivalTicks <= targetTicks)
            {
                ElapsedTicks = plan.ArrivalTicks;
                shipPosition = destinationPosition;
                travel = null;
                Record("arrival", ShipId, $"Pathfinder arrived at {destination.Name}. Awaiting orders.");
                return true;
            }
            // Always derive from the original departure, never accumulate per-step position errors.
            shipPosition = plan.DeparturePosition.Interpolate(destinationPosition,
                (double)(targetTicks - plan.DepartureTicks) / (plan.ArrivalTicks - plan.DepartureTicks));
        }
        ElapsedTicks = targetTicks;
        return false;
    }

    private void Record(string kind, string objectId, string message) =>
        events.Add(new(events.Count + 1L, ElapsedTicks, kind, objectId, message));
}
