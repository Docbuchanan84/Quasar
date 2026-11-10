namespace Quasar.Simulation;

public sealed record ChemicalEngine(double DryMassKg, double LengthM, double DiameterM,
    double ThrustN, double ExhaustVelocityMPerSecond, string PropellantId);

public sealed record PropellantTank(double DryMassKg, double CapacityKg, double QuantityKg, string PropellantId);

public sealed record PropulsionPerformance(double DryMassKg, double WetMassKg, double AvailablePropellantKg,
    double RatedMassFlowKgPerSecond, double InitialAccelerationMPerSecond2, double TheoreticalDeltaVMPerSecond);

public sealed record SpacecraftPropulsion(double BaseDryMassKg, ChemicalEngine Engine, PropellantTank Tank)
{
    public PropulsionPerformance CalculatePerformance()
    {
        if (Engine is null || Tank is null
            || !Positive(BaseDryMassKg) || !Positive(Engine.DryMassKg) || !Positive(Tank.DryMassKg)
            || !Positive(Engine.LengthM) || !Positive(Engine.DiameterM)
            || !Positive(Engine.ThrustN) || !Positive(Engine.ExhaustVelocityMPerSecond)
            || !Positive(Tank.CapacityKg) || !double.IsFinite(Tank.QuantityKg)
            || Tank.QuantityKg < 0 || Tank.QuantityKg > Tank.CapacityKg
            || string.IsNullOrWhiteSpace(Engine.PropellantId) || Engine.PropellantId != Tank.PropellantId)
            throw new ArgumentException("Invalid spacecraft mass, engine, or propellant storage.");

        var dryMass = BaseDryMassKg + Engine.DryMassKg + Tank.DryMassKg;
        var wetMass = dryMass + Tank.QuantityKg;
        var massFlow = Engine.ThrustN / Engine.ExhaustVelocityMPerSecond;
        var acceleration = Tank.QuantityKg == 0 ? 0 : Engine.ThrustN / wetMass;
        var deltaV = Engine.ExhaustVelocityMPerSecond * double.LogP1(Tank.QuantityKg / dryMass);
        if (!Positive(dryMass) || !Positive(wetMass) || !Positive(massFlow)
            || !double.IsFinite(acceleration) || !double.IsFinite(deltaV))
            throw new ArgumentException("Spacecraft propulsion performance exceeds the supported numeric range.");

        return new(dryMass, wetMass, Tank.QuantityKg, massFlow, acceleration, deltaV);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
