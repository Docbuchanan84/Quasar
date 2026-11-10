namespace Quasar.Simulation.Tests;

public sealed class SpacecraftPropulsionTests
{
    [Fact]
    public void PathfinderPerformanceIsReadOnlyAndRoundTrips()
    {
        var lab = new SystemLab();
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var performance = Assert.IsType<PropulsionPerformance>(lab.Snapshot().Ship.Propulsion);
        Assert.Equal(10_000, performance.DryMassKg);
        Assert.Equal(20_000, performance.WetMassKg);
        Assert.Equal(10_000, performance.AvailablePropellantKg);
        Assert.Equal(66.6666666667, performance.RatedMassFlowKgPerSecond, 8);
        Assert.Equal(10, performance.InitialAccelerationMPerSecond2);
        Assert.Equal(2079.44154168, performance.TheoreticalDeltaVMPerSecond, 7);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(before));
        Assert.Equal(performance, restored.Snapshot().Ship.Propulsion);
        Assert.Equal(before, SimulationStateJson.Serialize(restored.ExportState()));
    }

    [Fact]
    public void EngineAndFuelInputsDeterminePerformance()
    {
        var input = Pathfinder();
        var engine = input.Engine with { ThrustN = 100_000, ExhaustVelocityMPerSecond = 6000 };
        var performance = (input with { Engine = engine, Tank = input.Tank with { QuantityKg = 5000 } }).CalculatePerformance();
        Assert.Equal(15_000, performance.WetMassKg);
        Assert.Equal(5000, performance.AvailablePropellantKg);
        Assert.Equal(100_000d / 6000, performance.RatedMassFlowKgPerSecond);
        Assert.Equal(100_000d / 15_000, performance.InitialAccelerationMPerSecond2);
        Assert.Equal(6000 * Math.Log(1.5), performance.TheoreticalDeltaVMPerSecond, 8);

        var empty = (input with { Tank = input.Tank with { QuantityKg = 0 } }).CalculatePerformance();
        Assert.Equal(empty.DryMassKg, empty.WetMassKg);
        Assert.Equal(input.CalculatePerformance().RatedMassFlowKgPerSecond, empty.RatedMassFlowKgPerSecond);
        Assert.Equal(0, empty.InitialAccelerationMPerSecond2);
        Assert.Equal(0, empty.TheoreticalDeltaVMPerSecond);
    }

    [Fact]
    public void InvalidPhysicalInputsAreRejected()
    {
        var input = Pathfinder();
        SpacecraftPropulsion[] invalid =
        [
            input with { BaseDryMassKg = double.NaN },
            input with { BaseDryMassKg = -1 },
            input with { BaseDryMassKg = double.MaxValue, Engine = input.Engine with { DryMassKg = double.MaxValue } },
            input with { Engine = input.Engine with { ThrustN = double.PositiveInfinity } },
            input with { Engine = input.Engine with { LengthM = 0 } },
            input with { Engine = input.Engine with { ExhaustVelocityMPerSecond = double.Epsilon } },
            input with { Tank = input.Tank with { PropellantId = "incompatible" } },
            input with { Tank = input.Tank with { CapacityKg = 0 } },
            input with { Tank = input.Tank with { QuantityKg = -1 } },
            input with { Tank = input.Tank with { QuantityKg = 10_001 } },
        ];
        foreach (var propulsion in invalid)
            Assert.Throws<ArgumentException>(() => propulsion.CalculatePerformance());
    }

    [Fact]
    public void LegacyVersionCannotCarryPropulsion()
    {
        var state = new SystemLab().ExportState();
        var legacy = state.Simulation with { FormatVersion = 2 };
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(state with { Simulation = legacy }));
    }

    private static SpacecraftPropulsion Pathfinder() => new SystemLab().ExportState().Simulation.Spacecraft[0].Propulsion!;
}
