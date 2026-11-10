namespace Quasar.Simulation.Tests;

public sealed class PoweredTrajectoryTests
{
    private static readonly CelestialState Initial = new(new(150_000_000, 0, 0), new(0, 30, 0));
    private static readonly SpacecraftPropulsion Propulsion =
        new(8000, new(1000, 3, 1.5, 200_000, 3000, "test"), new(1000, 10_000, 10_000, "test"));

    [Fact]
    public void ExhaustionLimitsDurationAndMatchesTheVacuumRocketEquation()
    {
        var burn = new PoweredTrajectory(Initial, new(0, 1, 0), 1, Propulsion, 0, 3600);
        Assert.True(burn.ExhaustsPropellant);
        Assert.Equal(TimeSpan.FromSeconds(150).Ticks, burn.EndTicks);
        Assert.Equal(0, burn.RemainingPropellantKg(burn.EndTicks));
        var end = burn.StateAt(burn.EndTicks);
        var expected = Propulsion.CalculatePerformance().TheoreticalDeltaVMPerSecond / 1000;
        Assert.InRange(Math.Abs(end.VelocityKmPerSecond.Y - Initial.VelocityKmPerSecond.Y - expected), 0, 1e-8);
    }

    [Fact]
    public void TimedBurnIncludesGravityAndQueriesDoNotChangeItsState()
    {
        const double solarMu = 1.32712440018e11;
        var burn = new PoweredTrajectory(Initial, new(0, 1, 0), solarMu, Propulsion, 0, 10);
        Assert.False(burn.ExhaustsPropellant);
        var end = burn.StateAt(burn.EndTicks);
        Assert.True(end.VelocityKmPerSecond.X < 0);
        Assert.True(end.VelocityKmPerSecond.Y > Initial.VelocityKmPerSecond.Y);
        Assert.Equal(10_000 - 10 * Propulsion.CalculatePerformance().RatedMassFlowKgPerSecond,
            burn.RemainingPropellantKg(burn.EndTicks));
        Assert.Equal(Initial, burn.StateAt(0));
        Assert.Equal(end, burn.StateAt(burn.EndTicks));
    }

    [Fact]
    public void EmptyTankCannotIgnite()
    {
        var empty = Propulsion with { Tank = Propulsion.Tank with { QuantityKg = 0 } };
        Assert.Throws<InvalidOperationException>(() => new PoweredTrajectory(Initial, new(0, 1, 0), 1, empty, 0, 10));
    }
}
