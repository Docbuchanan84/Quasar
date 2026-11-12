namespace Quasar.Simulation.Tests;

public sealed class FusionTorchDesignerTests
{
    private static readonly FusionTorchDesign Alternate = new(240, 800, 2);

    [Fact]
    public void StockMatchesTheExistingDriveAndAlternateMakesARepeatableTestBurn()
    {
        var stock = FusionTorchDesigner.Preview(FusionTorchDesign.Stock);
        Assert.Equal(stock.Stock, stock.Draft);
        Assert.Equal(300_000, stock.Draft.ThrustN);
        Assert.Equal(1000, stock.Draft.DriveDryMassKg);
        Assert.Equal(0.3, stock.Draft.Performance.RatedMassFlowKgPerSecond);
        Assert.Equal(10, stock.Draft.Performance.InitialAccelerationMPerSecond2);

        var alternate = FusionTorchDesigner.Preview(Alternate).Draft;
        Assert.Equal(600_000, alternate.ThrustN);
        Assert.Equal(0.75, alternate.Performance.RatedMassFlowKgPerSecond);
        var test = FusionTorchDesigner.CompareBurn(Alternate);
        Assert.Equal(test, FusionTorchDesigner.CompareBurn(Alternate));
        Assert.Equal(30, test.DurationSeconds);
        Assert.Equal(9, test.Stock.ReactionMassUsedKg);
        Assert.Equal(22.5, test.Draft.ReactionMassUsedKg);
        Assert.True(test.Draft.VelocityChangeKmPerSecond > test.Stock.VelocityChangeKmPerSecond);
    }

    [Theory]
    [InlineData(double.NaN, 1000, 1.5, "Jet power")]
    [InlineData(150, 599, 1.5, "Exhaust speed")]
    [InlineData(150, 1000, 3, "Nozzle diameter")]
    [InlineData(240, 800, 1.5, "Widen it or lower jet power")]
    public void InvalidDesignsExplainHowToRecover(double power, double speed, double diameter, string message)
    {
        var design = new FusionTorchDesign(power, speed, diameter);
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => FusionTorchDesigner.Preview(design)).Message);
        Assert.Throws<ArgumentException>(() => FusionTorchDesigner.CompareBurn(design));
    }

    [Fact]
    public void RefitRejectsAnIncompatibleTank()
    {
        var lab = SystemLab.CreateChemicalFixture();
        var before = SimulationStateJson.Serialize(lab.ExportState());
        Assert.Throws<InvalidOperationException>(() => lab.FitDesignedTorch(Alternate));
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
    }
}
