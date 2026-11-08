namespace Quasar.Simulation;

// Kilometres in a right-handed system-local frame. Rendering libraries never own these values.
public readonly record struct Position3(double X, double Y, double Z)
{
    public double DistanceTo(Position3 other) => Math.Sqrt(
        Math.Pow(other.X - X, 2) + Math.Pow(other.Y - Y, 2) + Math.Pow(other.Z - Z, 2));

    public Position3 Interpolate(Position3 other, double fraction) => new(
        X + (other.X - X) * fraction, Y + (other.Y - Y) * fraction, Z + (other.Z - Z) * fraction);
}

public sealed record LabObject(string Id, string Name, string Kind, Position3 Position, string? ParentId);
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
    private readonly LabObject[] bodies =
    [
        new("star-helios", "Helios", "star", new(0, 0, 0), null),
        new("planet-iris", "Iris", "planet", new(1_200_000, 400_000, 200_000), "star-helios"),
        new("planet-nereid", "Nereid", "planet", new(-2_000_000, 1_300_000, -600_000), "star-helios")
    ];
    private readonly List<LabEvent> events = [];
    private Position3 shipPosition = new(900_000, -200_000, 350_000);
    private TravelPlan? travel;
    public long ElapsedTicks { get; private set; }

    public SystemLab() => Record("created", ShipId, "System Lab initialized. Pathfinder standing by.");

    public LabSnapshot Snapshot()
    {
        var destination = travel is null ? null : bodies.Single(body => body.Id == travel.DestinationId);
        return new(ElapsedTicks,
            [.. bodies, new(ShipId, "Pathfinder", "spacecraft", shipPosition, null)],
            new(travel is null ? "Idle" : "Underway", ShipSpeed, travel?.DestinationId,
                travel?.ArrivalTicks, destination is null ? 0 : shipPosition.DistanceTo(destination.Position)),
            [.. events]);
    }

    public void OrderTravel(string destinationId)
    {
        var destination = bodies.SingleOrDefault(body => body.Id == destinationId && body.Kind == "planet")
            ?? throw new ArgumentException("Choose a planet in this system.", nameof(destinationId));
        if (travel?.DestinationId == destinationId)
            throw new InvalidOperationException("Already travelling to that planet.");
        var distance = shipPosition.DistanceTo(destination.Position);
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
            var destination = bodies.Single(body => body.Id == plan.DestinationId);
            if (plan.ArrivalTicks <= targetTicks)
            {
                ElapsedTicks = plan.ArrivalTicks;
                shipPosition = destination.Position;
                travel = null;
                Record("arrival", ShipId, $"Pathfinder arrived at {destination.Name}. Awaiting orders.");
                return true;
            }
            // Always derive from the original departure, never accumulate per-step position errors.
            shipPosition = plan.DeparturePosition.Interpolate(destination.Position,
                (double)(targetTicks - plan.DepartureTicks) / (plan.ArrivalTicks - plan.DepartureTicks));
        }
        ElapsedTicks = targetTicks;
        return false;
    }

    private void Record(string kind, string objectId, string message) =>
        events.Add(new(events.Count + 1L, ElapsedTicks, kind, objectId, message));
}
