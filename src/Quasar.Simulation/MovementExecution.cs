namespace Quasar.Simulation;

public sealed record MovementEvent(long AtTicks, string SpacecraftId, string Kind);
public sealed record MovementView(string SpacecraftId, long AtTicks, CelestialState State, string Status,
    string? DestinationId, long? ArrivalTicks, double? RequiredDeltaVKmPerSecond, TransferFuelEstimate? Fuel,
    PropulsionPerformance? Propulsion = null, long? BurnEndTicks = null);

/// <summary>
/// Single-writer, headless movement owner. Advance considers queued boundaries and eligible analytical coasts.
/// Positions are pure timestamp queries. Cancellation retains momentum and resumes an unforced coast.
/// </summary>
public sealed partial class MovementExecution(CelestialPositionService catalogue)
{
    private sealed class Spacecraft(string primary, TwoBodyTrajectory? coast, SpacecraftPropulsion? propulsion)
    {
        public SpacecraftPropulsion? Propulsion = propulsion;
        public ActiveBurn? Burn;
        public string Primary = primary;
        public TwoBodyTrajectory? Coast = coast;
        public bool AutomaticPrimary;
        public TrajectoryPlan? Plan;
        public (TrajectoryDestination Destination, long ArrivalTicks)? Rendezvous;
        public long Generation;
        public int NextBoundary;
    }
    private readonly Dictionary<string, Spacecraft> spacecraft = new(StringComparer.Ordinal);
    private readonly PriorityQueue<(string Id, long Generation), (long Ticks, long Sequence)> boundaries = new();
    private long sequence;
    public long ElapsedTicks { get; private set; }

    public void AddSpacecraft(string id, CelestialState state, string primaryBodyId, SpacecraftPropulsion? propulsion = null,
        bool automaticPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(id) || spacecraft.ContainsKey(id)) throw new ArgumentException("Spacecraft identity must be unique and nonempty.");
        state.RequireFinite();
        var coast = NewCoast(state, primaryBodyId);
        propulsion?.CalculatePerformance();
        spacecraft.Add(id, new(primaryBodyId, coast, propulsion) { AutomaticPrimary = automaticPrimary });
    }

    public CelestialState StateAt(string id, long atTicks)
    {
        RequireFuture(atTicks);
        var ship = spacecraft[id];
        if (ship.Burn is { } burn)
        {
            if (atTicks > burn.Path.EndTicks)
                return PredictCoast(burn.EndCoast, ship.Primary, atTicks, burn.Path.EndTicks).State;
            return OrbitalNavigation.ToSystem(catalogue, ship.Primary, atTicks, burn.Path.StateAt(atTicks));
        }
        if (ship.Plan is { } plan)
        {
            if (plan.Finite is { } finite && atTicks > plan.ArrivalTicks)
                return PredictCoast(finite.ArrivalCoast, ship.Primary, atTicks, plan.ArrivalTicks).State;
            return atTicks <= plan.ArrivalTicks ? plan.StateAt(atTicks) : plan.Destination.StateAt(catalogue, atTicks);
        }
        if (ship.Rendezvous is { } target) return target.Destination.StateAt(catalogue, atTicks);
        return ship.AutomaticPrimary ? PredictCoast(ship.Coast!, ship.Primary, atTicks).State
            : OrbitalNavigation.ToSystem(catalogue, ship.Primary, atTicks, ship.Coast!.StateAt(atTicks));
    }

    private void RequireFuture(long atTicks)
    {
        if (atTicks < ElapsedTicks) throw new ArgumentOutOfRangeException(nameof(atTicks), "Execution queries describe current/future state, not discarded order history.");
    }

    public MovementView View(string id)
    {
        var ship = spacecraft[id]; var plan = ship.Plan;
        return new(id, ElapsedTicks, StateAt(id, ElapsedTicks),
            ship.Burn is not null ? "Burning" : plan is not null ? "Underway" : ship.Rendezvous is not null ? "Arrived" : "Coasting",
            plan?.TargetBodyId, plan?.ArrivalTicks, plan?.RequiredDeltaVKmPerSecond, plan?.Fuel, PropulsionAt(ship, ElapsedTicks)?.CalculatePerformance(),
            ship.Burn?.Path.EndTicks);
    }

    public TrajectoryPlan? CommittedPlan(string id) => spacecraft[id].Plan;

    public void Commit(string id, TrajectoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ship = spacecraft[id];
        if (ship.Burn is not null) throw new InvalidOperationException("Cut thrust before committing a course.");
        if (!ReferenceEquals(plan.Catalogue, catalogue))
            throw new InvalidOperationException("The celestial catalogue changed. Preview again before committing.");
        if (plan.DepartureTicks != ElapsedTicks || plan.InitialState != StateAt(id, ElapsedTicks))
            throw new InvalidOperationException("The departure state or time changed. Preview again before committing.");
        var propulsion = PropulsionAt(ship, ElapsedTicks);
        if (plan.Finite is { } finite && (finite.State.Propulsion != propulsion || finite.State.PrimaryBodyId != ship.Primary))
            throw new InvalidOperationException("The spacecraft propulsion or gravity primary changed. Preview again before committing.");
        ship.Propulsion = propulsion;
        ship.Plan = plan; ship.Rendezvous = null; ship.Generation++; ship.NextBoundary = 0;
        ship.Primary = plan.Segments[0].PrimaryBodyId;
        ship.Coast = null;
        ship.AutomaticPrimary = false;
        Schedule(id, ship);
        CompactQueue();
    }

    public void Cancel(string id)
    {
        var ship = spacecraft[id];
        if (ship.Plan is null) throw new InvalidOperationException("No committed course to cancel.");

        ship.Coast = NewCoast(StateAt(id, ElapsedTicks), ship.Primary);
        ship.Propulsion = PropulsionAt(ship, ElapsedTicks);
        ship.AutomaticPrimary = true;
        ship.Plan = null; ship.Generation++;
        CompactQueue();
    }

    /// <summary>Stop at the earliest boundary and handle every simultaneous event; caller decides whether to resume.</summary>
    public MovementEvent[] Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(duration.Ticks);
        var deadline = checked(ElapsedTicks + duration.Ticks);
        DiscardObsolete();
        var at = boundaries.TryPeek(out _, out var next) ? Math.Min(deadline, next.Ticks) : deadline;
        var patches = new List<(string Id, Spacecraft Ship, CoastPatch Patch)>();
        foreach (var (id, ship) in spacecraft)
        {
            if (!ship.AutomaticPrimary || ship.Coast is not { } coast) continue;
            var cursor = ElapsedTicks;
            while (cursor <= at && duration.Ticks > 0)
            {
                var through = cursor > long.MaxValue - SearchWindow ? at : Math.Min(at, cursor + SearchWindow);
                if (FindCoastPatch(coast, ship.Primary, cursor, through) is { } patch)
                {
                    at = patch.AtTicks;
                    patches.Add((id, ship, patch));
                    break;
                }
                if (through == at) break;
                cursor = through;
            }
        }
        var events = new List<MovementEvent>();
        foreach (var (id, ship, patch) in patches)
        {
            if (patch.AtTicks != at) continue;
            ship.Primary = patch.Primary;
            ship.Coast = patch.Coast;
            events.Add(new(at, id, "primary-change"));
        }
        while (boundaries.TryPeek(out var entry, out var priority) && priority.Ticks == at)
        {
            boundaries.Dequeue();
            var ship = spacecraft[entry.Id];
            if (entry.Generation != ship.Generation) continue;
            if (ship.Burn is { } burn)
            {
                EndBurn(ship, at, burn.EndCoast);
                events.Add(new(at, entry.Id, burn.Path.ExhaustsPropellant ? "burn-exhausted" : "burn-complete"));
                continue;
            }
            if (ship.Plan is not { } plan) continue;
            if (plan.Finite is { } finite)
            {
                if (at == finite.Braking.EndTicks)
                {
                    ship.Propulsion = PropulsionAt(ship, at);
                    ship.Coast = finite.ArrivalCoast;
                    ship.Plan = null;
                    ship.AutomaticPrimary = true;
                    events.Add(new(at, entry.Id, "course-arrival"));
                }
                else
                {
                    ship.NextBoundary++;
                    events.Add(new(at, entry.Id, at == finite.Departure.EndTicks ? "course-departure-cutoff" : "course-braking-start"));
                    Schedule(entry.Id, ship);
                }
                continue;
            }
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

    private static long? BoundaryTicks(Spacecraft ship) => ship.Plan?.Finite is { } finite
        ? ship.NextBoundary switch { 0 => finite.Departure.EndTicks, 1 => finite.Braking.StartTicks, _ => finite.Braking.EndTicks }
        : ship.Burn?.Path.EndTicks ?? ship.Plan?.Segments[ship.NextBoundary].EndTicks;

    private void Schedule(string id, Spacecraft ship)
    {
        if (BoundaryTicks(ship) is { } ticks)
            boundaries.Enqueue((id, ship.Generation), (ticks, sequence++));
    }

    private void DiscardObsolete()
    {
        while (boundaries.TryPeek(out var entry, out _)
            && (spacecraft[entry.Id].Generation != entry.Generation || BoundaryTicks(spacecraft[entry.Id]) is null))
            boundaries.Dequeue();
    }

    private void CompactQueue()
    {
        if (boundaries.Count <= spacecraft.Count * 2 + 1024) return;
        boundaries.Clear();
        foreach (var (id, ship) in spacecraft.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Schedule(id, ship);
    }
}
