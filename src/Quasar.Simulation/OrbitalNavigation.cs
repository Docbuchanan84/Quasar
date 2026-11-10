using System.Text.Json.Serialization;

namespace Quasar.Simulation;

public sealed record OrbitalTargetState(string Kind, string TargetId, string ReferenceBodyId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long AtTicks, CelestialState State)
{
    /// <summary>A frozen arrival sample for the existing advisory Lambert solver.</summary>
    public TrajectoryDestination ToAdvisoryDestination() => new(null, State, AtTicks);
}

public static class OrbitalNavigation
{
    public static CelestialState ToLocal(ICelestialPositionService catalogue, string bodyId, long atTicks, CelestialState state) =>
        TrajectoryPlanner.Subtract(state, catalogue.GetState(bodyId, atTicks));

    public static CelestialState ToSystem(ICelestialPositionService catalogue, string bodyId, long atTicks, CelestialState state) =>
        TrajectoryPlanner.Add(state, catalogue.GetState(bodyId, atTicks));

    public static OrbitalTargetState ResolveOrbit(CelestialPositionService catalogue, string primaryId,
        long epochTicks, CelestialState initialLocal, long atTicks)
    {
        var primary = catalogue.Bodies[primaryId];
        var environment = catalogue.GetEnvironment(primaryId);
        var coast = new TwoBodyTrajectory(initialLocal, epochTicks, environment.GravitationalParameterKm3PerSecond2);
        if (coast.ApoapsisKm is not { } apoapsis || !double.IsFinite(apoapsis) || coast.PeriapsisKm <= primary.RadiusKm
            || (environment.SphereOfInfluenceRadiusKm is { } sphere && apoapsis >= sphere))
            throw new ArgumentException("Choose a bound orbit above the reference surface and inside the modeled sphere of influence.");
        return new("orbit", primaryId, primaryId, atTicks, ToSystem(catalogue, primaryId, atTicks, coast.StateAt(atTicks)));
    }

    public static OrbitalTargetState ResolveStation(CelestialPositionService catalogue, string stationId, long atTicks)
    {
        if (!catalogue.Bodies.TryGetValue(stationId, out var station) || station.Kind != CelestialBodyKind.Station
            || station.ParentId is not { } primaryId)
            throw new ArgumentException("Choose a modeled station with a reference body.", nameof(stationId));
        return new("station", stationId, primaryId, atTicks, catalogue.GetState(stationId, atTicks));
    }
}
