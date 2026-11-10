namespace Quasar.Simulation;

public sealed record MovementEvent(long AtTicks, string SpacecraftId, string Kind);
public sealed record MovementView(string SpacecraftId, long AtTicks, CelestialState State, string Status,
    string? DestinationId, long? ArrivalTicks, double? RequiredDeltaVKmPerSecond, TransferFuelEstimate? Fuel,
    PropulsionPerformance? Propulsion = null);

/// <summary>
/// Single-writer, headless movement owner. Advance examines ordered boundaries, not every spacecraft.
/// Positions are pure timestamp queries. Cancellation retains momentum and resumes an unforced coast.
/// </summary>
public sealed partial class MovementExecution(CelestialPositionService catalogue)
{
    private sealed class Spacecraft(string primary, TwoBodyTrajectory? coast, SpacecraftPropulsion? propulsion)
    {
        public SpacecraftPropulsion? Propulsion { get; } = propulsion;
        public string Primary = primary;
        public TwoBodyTrajectory? Coast = coast;
        public TrajectoryPlan? Plan;
        public (TrajectoryDestination Destination, long ArrivalTicks)? Rendezvous;
        public long Generation;
        public int NextBoundary;
    }
    private readonly Dictionary<string, Spacecraft> spacecraft = new(StringComparer.Ordinal);
    private readonly PriorityQueue<(string Id, long Generation), (long Ticks, long Sequence)> boundaries = new();
    private long sequence;
    public long ElapsedTicks { get; private set; }

    public void AddSpacecraft(string id, CelestialState state, string primaryBodyId, SpacecraftPropulsion? propulsion = null)
    {
        if (string.IsNullOrWhiteSpace(id) || spacecraft.ContainsKey(id)) throw new ArgumentException("Spacecraft identity must be unique and nonempty.");
        state.RequireFinite();
        var coast = NewCoast(state, primaryBodyId);
        propulsion?.CalculatePerformance();
        spacecraft.Add(id, new(primaryBodyId, coast, propulsion));
    }

    public CelestialState StateAt(string id, long atTicks)
    {
        if (atTicks < ElapsedTicks) throw new ArgumentOutOfRangeException(nameof(atTicks), "Execution queries describe current/future state, not discarded order history.");
        var ship = spacecraft[id];
        if (ship.Plan is { } plan)
            return atTicks <= plan.ArrivalTicks ? plan.StateAt(atTicks) : plan.Destination.StateAt(catalogue, atTicks);
        if (ship.Rendezvous is { } target) return target.Destination.StateAt(catalogue, atTicks);
        return TrajectoryPlanner.Add(ship.Coast!.StateAt(atTicks), catalogue.GetState(ship.Primary, atTicks));
    }

    public MovementView View(string id)
    {
        var ship = spacecraft[id]; var plan = ship.Plan;
        return new(id, ElapsedTicks, StateAt(id, ElapsedTicks),
            plan is not null ? "Underway" : ship.Rendezvous is not null ? "Arrived" : "Coasting",
            plan?.Destination.BodyId, plan?.ArrivalTicks, plan?.RequiredDeltaVKmPerSecond, plan?.Fuel, ship.Propulsion?.CalculatePerformance());
    }

    public TrajectoryPlan? CommittedPlan(string id) => spacecraft[id].Plan;

    public void Commit(string id, TrajectoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ship = spacecraft[id];
        if (!ReferenceEquals(plan.Catalogue, catalogue))
            throw new InvalidOperationException("The celestial catalogue changed. Preview again before committing.");
        if (plan.DepartureTicks != ElapsedTicks || plan.InitialState != StateAt(id, ElapsedTicks))
            throw new InvalidOperationException("The departure state or time changed. Preview again before committing.");
        ship.Plan = plan; ship.Rendezvous = null; ship.Generation++; ship.NextBoundary = 0;
        ship.Primary = plan.Segments[0].PrimaryBodyId;
        ship.Coast = null;
        Schedule(id, ship);
        CompactQueue();
    }

    public void Cancel(string id)
    {
        var ship = spacecraft[id];
        if (ship.Plan is null) throw new InvalidOperationException("No committed course to cancel.");

        ship.Coast = NewCoast(StateAt(id, ElapsedTicks), ship.Primary);
        ship.Plan = null; ship.Generation++;
        CompactQueue();
    }

    /// <summary>Stop at the earliest boundary and handle every simultaneous event; caller decides whether to resume.</summary>
    public MovementEvent[] Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(duration.Ticks);
        var deadline = checked(ElapsedTicks + duration.Ticks);
        DiscardObsolete();
        if (!boundaries.TryPeek(out _, out var next) || next.Ticks > deadline)
        { ElapsedTicks = deadline; return []; }
        var at = next.Ticks;
        var events = new List<MovementEvent>();
        while (boundaries.TryPeek(out var entry, out var priority) && priority.Ticks == at)
        {
            boundaries.Dequeue();
            var ship = spacecraft[entry.Id];
            if (entry.Generation != ship.Generation || ship.Plan is not { } plan) continue;
            ship.NextBoundary++;
            if (ship.NextBoundary == plan.Segments.Count)
            {
                ship.Rendezvous = (plan.Destination, at); ship.Plan = null;
                events.Add(new(at, entry.Id, "arrival"));
            }
            else
            {
                ship.Primary = plan.Segments[ship.NextBoundary].PrimaryBodyId;
                events.Add(new(at, entry.Id, "patch"));
                Schedule(entry.Id, ship);
            }
        }
        ElapsedTicks = at;
        return [.. events.OrderBy(e => e.SpacecraftId, StringComparer.Ordinal)];
    }

    private TwoBodyTrajectory NewCoast(CelestialState state, string primary) => new(
        TrajectoryPlanner.Subtract(state, catalogue.GetState(primary, ElapsedTicks)), ElapsedTicks,
        catalogue.GetEnvironment(primary).GravitationalParameterKm3PerSecond2);

    private void Schedule(string id, Spacecraft ship) => boundaries.Enqueue((id, ship.Generation),
        (ship.Plan!.Segments[ship.NextBoundary].EndTicks, sequence++));

    private void DiscardObsolete()
    {
        while (boundaries.TryPeek(out var entry, out _)
            && (spacecraft[entry.Id].Generation != entry.Generation || spacecraft[entry.Id].Plan is null))
            boundaries.Dequeue();
    }

    private void CompactQueue()
    {
        if (boundaries.Count <= spacecraft.Count * 2 + 1024) return;
        boundaries.Clear();
        foreach (var (id, ship) in spacecraft.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            if (ship.Plan is not null) Schedule(id, ship);
    }
}
