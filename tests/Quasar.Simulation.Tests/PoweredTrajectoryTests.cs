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
    public void SolarBurnMatchesFineStepReferenceThroughDepletion()
    {
        var burn = new PoweredTrajectory(Initial, new(0, 1, 0), 1.32712440018e11, Propulsion, 0, 150);
        var end = burn.StateAt(burn.EndTicks);
        // Independent 0.01-second RK4 reference at 150 million km under solar gravity.
        var referencePosition = new Position3(149_999_999.93364376, 4638.08376807518, 0);
        var referenceVelocity = new Vector3d(-0.0008847495999650933, 32.07944152815246, 0);
        Assert.InRange(end.PositionKm.DistanceTo(referencePosition), 0, 1e-6);
        Assert.InRange((Vector3d.From(end.VelocityKmPerSecond) - referenceVelocity).Length, 0, 1e-8);
    }

    [Fact]
    public void FractionalDepletionRoundsUpToTheFirstTickWithoutFuel()
    {
        var partial = Propulsion with { Tank = Propulsion.Tank with { QuantityKg = 1.234501 } };
        var burn = new PoweredTrajectory(Initial, new(0, 1, 0), 1, partial, 0, 1);
        Assert.Equal(185_176, burn.EndTicks);
        Assert.True(burn.RemainingPropellantKg(burn.EndTicks - 1) > 0);
        Assert.Equal(0, burn.RemainingPropellantKg(burn.EndTicks));
    }

    [Fact]
    public void EmptyTankCannotIgnite()
    {
        var empty = Propulsion with { Tank = Propulsion.Tank with { QuantityKg = 0 } };
        Assert.Throws<InvalidOperationException>(() => new PoweredTrajectory(Initial, new(0, 1, 0), 1, empty, 0, 10));
    }
}
