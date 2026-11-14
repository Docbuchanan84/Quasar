using System.Text.Json.Serialization;

namespace Quasar.Simulation;

// The component slot also accepts drives that have no engine or propellant tank.
internal interface IPropulsionComponent
{
    void Validate();
    NavigationOptions Navigation { get; }
    Func<CancellationToken, DestinationRoutes> CaptureDestination(DestinationPlanningContext context);
}

public sealed record ChemicalEngine(double DryMassKg, double LengthM, double DiameterM,
    double ThrustN, double ExhaustVelocityMPerSecond, string PropellantId) : IReactionDrive
{
    int IReactionDrive.MaximumBurnSeconds => 86400;
    double IReactionDrive.MaximumStepSeconds => 1;
}

internal interface IReactionDrive
{
    double DryMassKg { get; }
    double LengthM { get; }
    double DiameterM { get; }
    double ThrustN { get; }
    double ExhaustVelocityMPerSecond { get; }
    string PropellantId { get; }
    int MaximumBurnSeconds { get; }
    double MaximumStepSeconds { get; }
}

/// <summary>An electric thruster and its installed fission-electric power supply.</summary>
public sealed record FissionElectricDrive(double DryMassKg, double LengthM, double DiameterM,
    double ElectricalPowerMw, double Efficiency, double ExhaustVelocityMPerSecond, string PropellantId) : IReactionDrive
{
    [JsonIgnore]
    public double ThrustN => 2_000_000 * ElectricalPowerMw * Efficiency / ExhaustVelocityMPerSecond;
    int IReactionDrive.MaximumBurnSeconds => 15 * 86400;
    double IReactionDrive.MaximumStepSeconds => 300;
}

public sealed record PropellantTank(double DryMassKg, double CapacityKg, double QuantityKg, string PropellantId);

public sealed record PropulsionPerformance(double DryMassKg, double WetMassKg, double AvailablePropellantKg,
    double RatedMassFlowKgPerSecond, double InitialAccelerationMPerSecond2, double TheoreticalDeltaVMPerSecond);

public sealed record SpacecraftPropulsion(double BaseDryMassKg, ChemicalEngine? Engine, PropellantTank Tank,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FissionElectricDrive? ElectricDrive = null) : IPropulsionComponent
{
    internal IReactionDrive Drive => (IReactionDrive?)Engine ?? ElectricDrive
        ?? throw new ArgumentException("A propellant-driven spacecraft needs an installed drive.");

    void IPropulsionComponent.Validate() => CalculatePerformance();
    NavigationOptions IPropulsionComponent.Navigation => CourseNavigation;
    private static readonly NavigationOptions CourseNavigation = new(Array.AsReadOnly(new[] { "approach", "orbit" }), "propellant", true);
    Func<CancellationToken, DestinationRoutes> IPropulsionComponent.CaptureDestination(DestinationPlanningContext context) =>
        context.Lab.CaptureRocketDestination(context.Order);

    public PropulsionPerformance CalculatePerformance()
    {
        var drive = Drive;
        if ((Engine is not null && ElectricDrive is not null) || Tank is null
            || ElectricDrive is { } electric && (!Positive(electric.ElectricalPowerMw) || electric.Efficiency is <= 0 or > 1)
            || !Positive(BaseDryMassKg) || !Positive(drive.DryMassKg) || !Positive(Tank.DryMassKg)
            || !Positive(drive.LengthM) || !Positive(drive.DiameterM)
            || !Positive(drive.ThrustN) || !Positive(drive.ExhaustVelocityMPerSecond)
            || !Positive(Tank.CapacityKg) || !double.IsFinite(Tank.QuantityKg)
            || Tank.QuantityKg < 0 || Tank.QuantityKg > Tank.CapacityKg
            || string.IsNullOrWhiteSpace(drive.PropellantId) || drive.PropellantId != Tank.PropellantId)
            throw new ArgumentException("Invalid spacecraft mass, engine, or propellant storage.");

        var dryMass = BaseDryMassKg + drive.DryMassKg + Tank.DryMassKg;
        var wetMass = dryMass + Tank.QuantityKg;
        var massFlow = drive.ThrustN / drive.ExhaustVelocityMPerSecond;
        var acceleration = Tank.QuantityKg == 0 ? 0 : drive.ThrustN / wetMass;
        var deltaV = drive.ExhaustVelocityMPerSecond * double.LogP1(Tank.QuantityKg / dryMass);
        if (!Positive(dryMass) || !Positive(wetMass) || !Positive(massFlow)
            || !double.IsFinite(acceleration) || !double.IsFinite(deltaV))
            throw new ArgumentException("Spacecraft propulsion performance exceeds the supported numeric range.");

        return new(dryMass, wetMass, Tank.QuantityKg, massFlow, acceleration, deltaV);
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
