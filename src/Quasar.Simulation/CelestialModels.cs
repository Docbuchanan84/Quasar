namespace Quasar.Simulation;

/// <summary>Kilometres in a right-handed system-local frame, independent of rendering libraries.</summary>
public readonly record struct Position3(double X, double Y, double Z)
{
    public double DistanceTo(Position3 other) => Math.Sqrt(
        Math.Pow(other.X - X, 2) + Math.Pow(other.Y - Y, 2) + Math.Pow(other.Z - Z, 2));

    public Position3 Interpolate(Position3 other, double fraction) => new(
        X + (other.X - X) * fraction, Y + (other.Y - Y) * fraction, Z + (other.Z - Z) * fraction);
}

/// <summary>Right-handed system-local velocity, in kilometres per simulation second.</summary>
public readonly record struct Velocity3(double X, double Y, double Z);

/// <summary>Position in km and velocity in km/s, both in the same inertial XYZ frame.</summary>
public readonly record struct CelestialState(Position3 PositionKm, Velocity3 VelocityKmPerSecond)
{
    internal void RequireFinite()
    {
        if (!double.IsFinite(PositionKm.X) || !double.IsFinite(PositionKm.Y) || !double.IsFinite(PositionKm.Z)
            || !double.IsFinite(VelocityKmPerSecond.X) || !double.IsFinite(VelocityKmPerSecond.Y)
            || !double.IsFinite(VelocityKmPerSecond.Z)) throw new ArgumentOutOfRangeException(nameof(CelestialState), "State must be finite.");
    }
}

public enum CelestialBodyKind { Star, Planet, Moon, Asteroid, Other }

/// <summary>
/// Immutable simulation data. Without an orbit, ReferenceState is a linear parent-relative
/// state at ReferenceEpochTicks (zero velocity gives a fixed offset). An orbit replaces it.
/// GravitationalParameterKm3PerSecond2 is this body's GM, not its parent's GM.
/// </summary>
public sealed record CelestialBody(string Id, string Name, CelestialBodyKind Kind,
    double GravitationalParameterKm3PerSecond2, string? ParentId = null,
    KeplerianOrbit? Orbit = null, CelestialState ReferenceState = default, long ReferenceEpochTicks = 0);

/// <summary>Elliptic elements relative to the parent's focus; all angles are radians.</summary>
public sealed record KeplerianOrbit(double SemiMajorAxisKm, double Eccentricity,
    double InclinationRadians = 0, double LongitudeOfAscendingNodeRadians = 0,
    double ArgumentOfPeriapsisRadians = 0, double MeanAnomalyAtEpochRadians = 0, long EpochTicks = 0);

/// <summary>Derived two-body metadata. SOI is the approximate Laplace radius, not a force boundary.</summary>
public sealed record OrbitalEnvironment(double GravitationalParameterKm3PerSecond2,
    double? RelativeGravitationalParameterKm3PerSecond2, double? PeriodSeconds, double? SphereOfInfluenceRadiusKm);

public interface ICelestialPositionService
{
    CelestialState GetState(string bodyId, long atTicks);
    IReadOnlyDictionary<string, CelestialState> GetStates(long atTicks);
    OrbitalEnvironment GetEnvironment(string bodyId);
}
