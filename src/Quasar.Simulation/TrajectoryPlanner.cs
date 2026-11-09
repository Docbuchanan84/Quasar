using System.Collections.ObjectModel;

namespace Quasar.Simulation;

/// <summary>A celestial ephemeris target, or a fixed/constant-velocity inertial waypoint.</summary>
public sealed record TrajectoryDestination(string? BodyId, CelestialState ReferenceState = default, long EpochTicks = 0)
{
    public CelestialState StateAt(ICelestialPositionService celestial, long atTicks)
    {
        if (BodyId is not null) return celestial.GetState(BodyId, atTicks);
        ReferenceState.RequireFinite();
        var seconds = (double)(((decimal)atTicks - EpochTicks) / TimeSpan.TicksPerSecond);
        var state = new CelestialState((Vector3d.From(ReferenceState.PositionKm)
            + Vector3d.From(ReferenceState.VelocityKmPerSecond) * seconds).Position, ReferenceState.VelocityKmPerSecond);
        state.RequireFinite();
        return state;
    }
}

public sealed record TransferLeg(string PrimaryBodyId, long ArrivalTicks, TrajectoryDestination Destination,
    bool LongWay = false, double MinimumPrimaryClearanceKm = 1);
public sealed record TransferRequest(CelestialState InitialState, long DepartureTicks, TransferLeg Leg,
    double? MaximumDeltaVKmPerSecond = null);
public sealed record TrajectoryBurn(long AtTicks, string Phase, Velocity3 DeltaVelocityKmPerSecond)
{
    public double DeltaVKmPerSecond => Vector3d.From(DeltaVelocityKmPerSecond).Length;
}
public sealed record FlybyEncounter(string BodyId, double ClosestApproachKm);
public sealed record TrajectoryPoint(long AtTicks, CelestialState State);
public sealed record TransferFuelEstimate(double? PropellantMassKg, string? UnavailableReason);

/// <summary>Future propulsion integration; estimates never debit inventory or imply a fuel model exists.</summary>
public interface ITransferFuelEstimator
{
    TransferFuelEstimate Estimate(double requiredDeltaVKmPerSecond);
}

public sealed class TrajectorySegment
{
    internal TrajectorySegment(CelestialPositionService catalogue, string primaryId, TwoBodyTrajectory coast,
        long endTicks)
    { Catalogue = catalogue; PrimaryBodyId = primaryId; Coast = coast; EndTicks = endTicks; }
    internal CelestialPositionService Catalogue { get; }
    public string PrimaryBodyId { get; }
    public TwoBodyTrajectory Coast { get; }
    public long StartTicks => Coast.EpochTicks;
    public long EndTicks { get; }
    public CelestialState StateAt(long atTicks)
    {
        if (atTicks < StartTicks || atTicks > EndTicks) throw new ArgumentOutOfRangeException(nameof(atTicks));
        var primary = Catalogue.GetState(PrimaryBodyId, atTicks);
        var local = Coast.StateAt(atTicks);
        return TrajectoryPlanner.Add(local, primary);
    }
}

/// <summary>Immutable solved parameters. Sampling is disposable presentation data, never a physics input.</summary>
public sealed class TrajectoryPlan
{
    internal TrajectoryPlan(CelestialPositionService catalogue, CelestialState initial,
        TrajectoryDestination destination, TrajectorySegment[] segments, TrajectoryBurn[] burns,
        TransferFuelEstimate fuel, FlybyEncounter? flyby = null)
    {
        Catalogue = catalogue; InitialState = initial; Destination = destination;
        Segments = Array.AsReadOnly(segments); Burns = Array.AsReadOnly(burns); Fuel = fuel;
        Flyby = flyby;
        RequiredDeltaVKmPerSecond = burns.Where(b => flyby is null || b.Phase != "patch").Sum(b => b.DeltaVKmPerSecond);
    }
    internal CelestialPositionService Catalogue { get; }
    public CelestialState InitialState { get; }
    public TrajectoryDestination Destination { get; }
    public ReadOnlyCollection<TrajectorySegment> Segments { get; }
    public ReadOnlyCollection<TrajectoryBurn> Burns { get; }
    public TransferFuelEstimate Fuel { get; }
    public FlybyEncounter? Flyby { get; }
    public long DepartureTicks => Segments[0].StartTicks;
    public long ArrivalTicks => Segments[^1].EndTicks;
    public double TravelSeconds => (double)(((decimal)ArrivalTicks - DepartureTicks) / TimeSpan.TicksPerSecond);
    public double RequiredDeltaVKmPerSecond { get; }

    /// <summary>Right-continuous at impulses: departure/patch velocity is post-burn; arrival matches the target.</summary>
    public CelestialState StateAt(long atTicks)
    {
        if (atTicks < DepartureTicks || atTicks > ArrivalTicks) throw new ArgumentOutOfRangeException(nameof(atTicks));
        if (atTicks == ArrivalTicks) return Destination.StateAt(Catalogue, atTicks);
        var low = 0; var high = Segments.Count - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (atTicks >= Segments[mid].EndTicks) low = mid + 1; else high = mid;
        }
        return Segments[low].StateAt(atTicks);
    }

    public TrajectoryPoint[] Sample(int pointCount = 65)
    {
        if (pointCount is < 2 or > 1025) throw new ArgumentOutOfRangeException(nameof(pointCount), "Choose 2 to 1025 samples.");
        // Always include patch epochs even when uniform samples miss them.
        var ticks = new SortedSet<long>(Segments.Select(s => s.StartTicks)) { ArrivalTicks };
        for (var i = 0; i < pointCount; i++)
            ticks.Add(DepartureTicks + (long)(((decimal)ArrivalTicks - DepartureTicks) * i / (pointCount - 1)));
        return ticks.Select(t => new TrajectoryPoint(t, StateAt(t))).ToArray();
    }
}

/// <summary>Pure boundary-value solving plus a bounded memo scoped to one immutable celestial catalogue.</summary>
public sealed partial class TrajectoryPlanner(CelestialPositionService catalogue, ITransferFuelEstimator? fuelEstimator = null)
{
    private readonly Dictionary<TransferRequest, TrajectoryPlan> cache = [];
    private readonly object gate = new();
    public CelestialPositionService Catalogue => catalogue;

    public TrajectoryPlan Plan(TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (cache.TryGetValue(request, out var plan)) return plan;
            plan = PlanPatched(request.InitialState, request.DepartureTicks, [request.Leg], request.MaximumDeltaVKmPerSecond);
            if (cache.Count >= 256) cache.Clear();
            cache.Add(request, plan);
            return plan;
        }
    }

    /// <summary>Explicit patched conics at supplied parent/child SOI waypoints, with costed correction impulses.</summary>
    public TrajectoryPlan PlanPatched(CelestialState initialState, long departureTicks,
        IReadOnlyList<TransferLeg> legs, double? maximumDeltaVKmPerSecond = null)
    {
        initialState.RequireFinite();
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count is < 1 or > 32) throw new ArgumentException("Supply 1 to 32 transfer legs.");
        if (maximumDeltaVKmPerSecond is { } budget && (!double.IsFinite(budget) || budget < 0))
            throw new ArgumentException("Delta-v budget must be finite and nonnegative.");
        var segments = new List<TrajectorySegment>(); var burns = new List<TrajectoryBurn>();
        var state = initialState; var start = departureTicks;
        foreach (var leg in legs)
        {
            ArgumentNullException.ThrowIfNull(leg);
            ArgumentNullException.ThrowIfNull(leg.Destination);
            if (leg.ArrivalTicks <= start || (decimal)leg.ArrivalTicks - start > TimeSpan.TicksPerDay * 36525m)
                throw new ArgumentException("Each leg needs positive flight time of at most 100 Julian years.");
            if (!double.IsFinite(leg.MinimumPrimaryClearanceKm) || leg.MinimumPrimaryClearanceKm <= 0)
                throw new ArgumentException("Primary clearance must be finite and positive.");
            if (!catalogue.Bodies.ContainsKey(leg.PrimaryBodyId)) throw new ArgumentException("Unknown gravitational primary.");
            if (leg.Destination.BodyId is { } body && !catalogue.Bodies.ContainsKey(body))
                throw new ArgumentException("Unknown destination body.");
            var mu = catalogue.GetEnvironment(leg.PrimaryBodyId).GravitationalParameterKm3PerSecond2;
            if (mu <= 0) throw new ArgumentException("The selected primary has no positive gravitational parameter.");
            if (segments.Count > 0 && segments[^1].PrimaryBodyId != leg.PrimaryBodyId)
                ValidatePatch(catalogue, segments[^1].PrimaryBodyId, leg.PrimaryBodyId, state.PositionKm, start);
            var primaryStart = catalogue.GetState(leg.PrimaryBodyId, start);
            var primaryEnd = catalogue.GetState(leg.PrimaryBodyId, leg.ArrivalTicks);
            var target = leg.Destination.StateAt(catalogue, leg.ArrivalTicks);
            var local = Subtract(state, primaryStart);
            var endPosition = Subtract(target, primaryEnd).PositionKm;
            var seconds = (double)(((decimal)leg.ArrivalTicks - start) / TimeSpan.TicksPerSecond);
            var velocity = LambertSolver.DepartureVelocity(local.PositionKm, endPosition, seconds, mu, leg.LongWay);
            var coast = new TwoBodyTrajectory(new(local.PositionKm, velocity), start, mu);
            // Conservative whole-conic clearance: can reject a safe arc whose distant periapsis is unsafe.
            if (coast.PeriapsisKm < leg.MinimumPrimaryClearanceKm)
                throw new ArgumentException("Transfer violates the minimum primary clearance; change time, arc or waypoint.");
            var segment = new TrajectorySegment(catalogue, leg.PrimaryBodyId, coast, leg.ArrivalTicks);
            var end = segment.StateAt(leg.ArrivalTicks);
            if (end.PositionKm.DistanceTo(target.PositionKm) > Math.Max(1e-5, Vector3d.From(endPosition).Length * 1e-8))
                throw new ArgumentException("Transfer failed endpoint verification; change time or geometry.");
            burns.Add(new(start, segments.Count == 0 ? "departure" : "patch",
                (Vector3d.From(velocity) - Vector3d.From(local.VelocityKmPerSecond)).Velocity));
            segments.Add(segment); state = end; start = leg.ArrivalTicks;
        }
        var destination = legs[^1].Destination;
        burns.Add(new(start, "arrival", (Vector3d.From(destination.StateAt(catalogue, start).VelocityKmPerSecond)
            - Vector3d.From(state.VelocityKmPerSecond)).Velocity));
        var deltaV = burns.Sum(b => b.DeltaVKmPerSecond);
        if (!double.IsFinite(deltaV)) throw new ArgumentException("Required delta-v exceeds finite range.");
        if (maximumDeltaVKmPerSecond is { } limit && deltaV > limit)
            throw new ArgumentException($"Required delta-v {deltaV:G6} km/s exceeds budget {limit:G6} km/s.");
        return new(catalogue, initialState, destination, [.. segments], [.. burns],
            fuelEstimator?.Estimate(deltaV) ?? new(null, "No implemented propulsion, spacecraft mass or propellant inventory model."));
    }

    internal static void ValidatePatch(CelestialPositionService catalogue, string oldPrimary, string newPrimary,
        Position3 position, long atTicks)
    {
        var oldBody = catalogue.Bodies[oldPrimary]; var newBody = catalogue.Bodies[newPrimary];
        var child = oldBody.ParentId == newPrimary ? oldBody : newBody.ParentId == oldPrimary ? newBody : null;
        if (child is null) throw new ArgumentException("A gravity patch must switch between a parent and its child.");
        var soi = catalogue.GetEnvironment(child.Id).SphereOfInfluenceRadiusKm;
        if (soi is null or <= 0 || Math.Abs(position.DistanceTo(catalogue.GetState(child.Id, atTicks).PositionKm) / soi.Value - 1) > .01)
            throw new ArgumentException("Gravity patches require a waypoint within 1% of the child's Laplace sphere of influence.");
    }

    internal static CelestialState Add(CelestialState a, CelestialState b) => new(
        (Vector3d.From(a.PositionKm) + Vector3d.From(b.PositionKm)).Position,
        (Vector3d.From(a.VelocityKmPerSecond) + Vector3d.From(b.VelocityKmPerSecond)).Velocity);
    internal static CelestialState Subtract(CelestialState a, CelestialState b) => new(
        (Vector3d.From(a.PositionKm) - Vector3d.From(b.PositionKm)).Position,
        (Vector3d.From(a.VelocityKmPerSecond) - Vector3d.From(b.VelocityKmPerSecond)).Velocity);
}
