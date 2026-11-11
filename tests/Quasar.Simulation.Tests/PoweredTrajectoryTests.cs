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

    [Theory]
    [InlineData(98.980, false)] // Crosses even the free-coast 99% entry radius.
    [InlineData(99.9999, false)]
    [InlineData(100.000001, false)] // The near miss is within the conservative error bound.
    [InlineData(100.001, true)]
    public void ValidatesClearanceBetweenPoweredSamplesOfAMovingSphere(double radialOffset, bool clear)
    {
        const double mu = 1.32712440018e11;
        const double orbitRadius = 150_000_000;
        const double sphere = 100;
        var childMu = mu * Math.Pow(sphere / orbitRadius, 2.5);
        var catalogue = new CelestialPositionService([
            new("star", "Star", CelestialBodyKind.Star, mu, RadiusKm: 695700),
            new("child", "Child", CelestialBodyKind.Asteroid, childMu, "star", new(orbitRadius, 0), RadiusKm: 1)
        ]);
        var initial = new CelestialState(new(orbitRadius + radialOffset, -50, 0),
            new(0, Math.Sqrt((mu + childMu) / orbitRadius) + 100, 0));
        var propulsion = Propulsion with { Engine = Propulsion.Engine with { ThrustN = 1 } };
        var burn = new PoweredTrajectory(initial, new(0, 1, 0), mu, propulsion, 0, 1);
        double Distance(long ticks) => burn.StateAt(ticks).PositionKm.DistanceTo(catalogue.GetState("child", ticks).PositionKm);

        Assert.True(Distance(0) > sphere && Distance(burn.EndTicks) > sphere);
        Assert.Equal(radialOffset, Distance(burn.EndTicks / 2), 4);
        Assert.Equal(clear, new MovementExecution(catalogue).PoweredPathIsClear(burn, "star", CancellationToken.None));
    }

    [Theory]
    [InlineData(98.98, true)]
    [InlineData(98.999, true)]
    [InlineData(99.001, false)]
    [InlineData(100.001, false)]
    public void PoweredCoursePatchesBetweenSamplesAndKeepsNearGrazesContinuous(double offset, bool enters)
    {
        const double mu = 1.32712440018e11;
        const double radius = 150_000_000;
        var childMu = mu * Math.Pow(100 / radius, 2.5);
        var catalogue = new CelestialPositionService([
            new("star", "Star", CelestialBodyKind.Star, mu, RadiusKm: 695700),
            new("child", "Child", CelestialBodyKind.Asteroid, childMu, "star", new(radius, 0), RadiusKm: 1)
        ]);
        var initial = new CelestialState(new(radius + offset, -50, 0), new(0, Math.Sqrt((mu + childMu) / radius) + 100, 0));
        var propulsion = Propulsion with { Engine = Propulsion.Engine with { ThrustN = 1 } };
        var burn = CourseBurn.Build(catalogue, initial, "star", 0, TimeSpan.TicksPerSecond, new(0, 1, 0), propulsion, CancellationToken.None);
        Assert.Equal(enters, burn.Parts.Any(part => part.Primary == "child"));
        Assert.Equal("star", burn.PrimaryAt(0));
        Assert.Equal("star", burn.PrimaryAt(burn.EndTicks));
        for (var i = 1; i < burn.Parts.Length; i++)
        {
            var part = burn.Parts[i];
            var prior = burn.Parts[i - 1];
            var ticks = part.Path.StartTicks;
            var from = OrbitalNavigation.ToSystem(catalogue, prior.Primary, ticks, prior.Path.StateAt(ticks));
            var to = OrbitalNavigation.ToSystem(catalogue, part.Primary, ticks, part.Path.StateAt(ticks));
            Assert.InRange(from.PositionKm.DistanceTo(to.PositionKm), 0, 1e-7);
            Assert.InRange((Vector3d.From(from.VelocityKmPerSecond) - Vector3d.From(to.VelocityKmPerSecond)).Length, 0, 1e-10);
        }
        Assert.Equal(propulsion.Tank.QuantityKg - propulsion.CalculatePerformance().RatedMassFlowKgPerSecond,
            burn.RemainingPropellantKg(burn.EndTicks), 9);
    }
}
