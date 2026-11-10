using System.Collections.Immutable;

namespace Quasar.Simulation;

/// <summary>Versioned physical state shared by the headless movement owner and the System Lab.</summary>
public sealed record SimulationState(int FormatVersion, long ElapsedTicks,
    ImmutableArray<CelestialBodyState> Bodies, ImmutableArray<SpacecraftState> Spacecraft)
{
    public const int CurrentFormatVersion = 3;

    public static bool SupportsFormatVersion(int version) => version is 1 or 2 or CurrentFormatVersion;

    internal void RequireSupportedVersion()
    {
        if (!SupportsFormatVersion(FormatVersion))
            throw new InvalidDataException($"Unsupported simulation state version {FormatVersion}.");
        if (FormatVersion < 3 && !Spacecraft.IsDefault && Spacecraft.Any(ship => ship?.Propulsion is not null))
            throw new InvalidDataException("Propulsion requires simulation state version 3.");
    }
}

public sealed record SystemLabState(SimulationState Simulation, long LastEventSequence,
    ImmutableArray<LabHistoryEntry> History);
public sealed record LabHistoryEntry(long Sequence, long AtTicks, string Kind, string ObjectId, string Message);

public sealed record CelestialBodyState(string Id, string Name, CelestialBodyKind Kind,
    double GravitationalParameterKm3PerSecond2, string? ParentId, OrbitState? Orbit,
    KinematicState ReferenceState, long ReferenceEpochTicks, double RadiusKm = 0);
public sealed record OrbitState(double SemiMajorAxisKm, double Eccentricity, double InclinationRadians,
    double LongitudeOfAscendingNodeRadians, double ArgumentOfPeriapsisRadians,
    double MeanAnomalyAtEpochRadians, long EpochTicks);

public sealed record SpacecraftState(string Id, string PrimaryBodyId, CoastState? FreeCoast,
    CommittedTrajectoryState? Trajectory, RendezvousState? Rendezvous,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SpacecraftPropulsion? Propulsion = null);
public sealed record CoastState(long EpochTicks, KinematicState InitialRelativeState);
public sealed record CommittedTrajectoryState(KinematicState InitialState, DestinationState Destination,
    ImmutableArray<TrajectorySegmentState> Segments, ImmutableArray<TrajectoryBurnState> Burns,
    FuelEstimateState Fuel, int NextBoundaryIndex, FlybyEncounter? Flyby = null);
public sealed record TrajectorySegmentState(string PrimaryBodyId, CoastState Coast, long EndTicks);
public sealed record TrajectoryBurnState(long AtTicks, string Phase, CartesianState DeltaVelocityKmPerSecond);
public sealed record DestinationState(string? BodyId, KinematicState ReferenceState, long EpochTicks);
public sealed record RendezvousState(DestinationState Destination, long ArrivalTicks);
public sealed record FuelEstimateState(double? PropellantMassKg, string? UnavailableReason);

public sealed record KinematicState(CartesianState PositionKm, CartesianState VelocityKmPerSecond)
{
    internal static KinematicState From(CelestialState state) => new(
        new(state.PositionKm.X, state.PositionKm.Y, state.PositionKm.Z), CartesianState.From(state.VelocityKmPerSecond));

    internal CelestialState ToRuntime()
    {
        if (PositionKm is null || VelocityKmPerSecond is null)
            throw new InvalidDataException("Missing position or velocity.");
        var state = new CelestialState(new(PositionKm.X, PositionKm.Y, PositionKm.Z), VelocityKmPerSecond.ToVelocity());
        state.RequireFinite();
        return state;
    }
}

public sealed record CartesianState(double X, double Y, double Z)
{
    internal static CartesianState From(Velocity3 value) => new(value.X, value.Y, value.Z);
    internal Velocity3 ToVelocity() => new(X, Y, Z);
}
