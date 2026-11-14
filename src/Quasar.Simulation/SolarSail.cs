namespace Quasar.Simulation;

/// <summary>A flat reflecting sail. Thrust follows sunlight, attitude and area-to-mass ratio.</summary>
public sealed record SolarSail(double HullMassKg, double SailMassKg, double AreaSquareMeters) : IPropulsionComponent
{
    internal const double AstronomicalUnitKm = 149_597_870.7;
    private const double ReflectedPressureAtOneAu = 9.08e-6;
    public double TotalMassKg => HullMassKg + SailMassKg;

    void IPropulsionComponent.Validate()
    {
        if (!double.IsFinite(TotalMassKg) || HullMassKg <= 0 || SailMassKg <= 0
            || !double.IsFinite(AreaSquareMeters) || AreaSquareMeters <= 0
            || AreaSquareMeters / TotalMassKg > 100)
            throw new ArgumentException("Invalid solar sail area or spacecraft mass.");
    }

    internal static void ValidateAngle(double angleDegrees)
    {
        if (!double.IsFinite(angleDegrees) || Math.Abs(angleDegrees) > 80)
            throw new ArgumentException("Choose a sail angle from −80 to +80 degrees.");
    }

    internal double FaceOnAcceleration(double distanceKm) =>
        ReflectedPressureAtOneAu * AreaSquareMeters / TotalMassKg * Math.Pow(AstronomicalUnitKm / distanceKm, 2);

    internal Vector3d Acceleration(Vector3d sunToShip, Vector3d heliocentricVelocity, double angleDegrees)
    {
        var distance = sunToShip.Length;
        var radial = sunToShip / distance;
        var angle = angleDegrees * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var normal = radial * cosine;
        if (angleDegrees != 0)
        {
            var tangent = heliocentricVelocity - radial * Vector3d.Dot(heliocentricVelocity, radial);
            if (!(tangent.Length > 1e-9))
                throw new InvalidOperationException("Sail trim needs a transverse direction around the Sun.");
            normal += tangent / tangent.Length * Math.Sin(angle);
        }
        return normal * (FaceOnAcceleration(distance) * cosine * cosine / 1000);
    }
}
