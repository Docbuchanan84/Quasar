using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Quasar.Simulation;

/// <summary>Versioned physical state shared by the headless movement owner and the System Lab.</summary>
public sealed record SimulationState(int FormatVersion, long ElapsedTicks,
    ImmutableArray<CelestialBodyState> Bodies, ImmutableArray<SpacecraftState> Spacecraft)
{
    public const int CurrentFormatVersion = 6;

    public static bool SupportsFormatVersion(int version) => version is >= 1 and <= CurrentFormatVersion;

    internal void RequireSupportedVersion()
    {
        if (!SupportsFormatVersion(FormatVersion))
            throw new InvalidDataException($"Unsupported simulation state version {FormatVersion}.");
        if (FormatVersion < 3 && !Spacecraft.IsDefault && Spacecraft.Any(ship => ship?.Propulsion is not null))
            throw new InvalidDataException("Propulsion requires simulation state version 3.");
        if (FormatVersion < 4 && !Spacecraft.IsDefault && Spacecraft.Any(ship => ship?.ActiveBurn is not null))
            throw new InvalidDataException("Active burns require simulation state version 4.");
        if (FormatVersion < 5 && !Spacecraft.IsDefault && Spacecraft.Any(ship => ship?.FreeCoast?.AutomaticPrimary == true))
            throw new InvalidDataException("Automatic gravity transitions require simulation state version 5.");
        if (FormatVersion < 6 && !Spacecraft.IsDefault && Spacecraft.Any(ship => ship?.FiniteCourse is not null))
            throw new InvalidDataException("Finite courses require simulation state version 6.");
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SpacecraftPropulsion? Propulsion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ActiveBurnState? ActiveBurn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FiniteCourseState? FiniteCourse = null);
public sealed record FiniteCourseState(string PrimaryBodyId, string TargetId, string ArrivalKind,
    long DepartureTicks, long ArrivalTicks, KinematicState InitialState, KinematicState TargetLocalState,
    SpacecraftPropulsion Propulsion, CartesianState DepartureDirection, long DepartureDurationTicks,
    CartesianState BrakingDirection, long BrakingDurationTicks);
public sealed record ActiveBurnState(long StartTicks, int DurationSeconds, KinematicState InitialRelativeState,
    CartesianState UnitDirection, double IgnitionPropellantKg);
public sealed record CoastState(long EpochTicks, KinematicState InitialRelativeState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AutomaticPrimary = false);
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
