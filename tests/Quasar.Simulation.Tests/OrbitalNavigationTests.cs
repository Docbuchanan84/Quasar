namespace Quasar.Simulation.Tests;

public sealed class OrbitalNavigationTests
{
    [Theory]
    [InlineData("moon-moon")]
    [InlineData("station-staryard")]
    public void EarthCenteredStateRoundTripsWithPositionAndVelocity(string id)
    {
        var catalogue = SolarSystem.CreateCatalogue();
        const long ticks = 123_456_789_000;
        var inertial = catalogue.GetState(id, ticks);
        var local = OrbitalNavigation.ToLocal(catalogue, SolarSystem.Earth, ticks, inertial);
        AssertNear(inertial, OrbitalNavigation.ToSystem(catalogue, SolarSystem.Earth, ticks, local));
        Assert.True(Vector3d.From(local.VelocityKmPerSecond).Length < 5);
        if (id == "station-staryard")
            Assert.Equal(inertial, OrbitalNavigation.ResolveStation(catalogue, id, ticks).State);
    }

    [Theory]
    [InlineData("planet-earth", 400)]
    [InlineData("moon-moon", 100)]
    public void TargetPresetReturnsOrbitalVelocityWithoutChangingTheWorld(string bodyId, double altitude)
    {
        var lab = new SystemLab();
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var target = lab.TargetStateAt("orbit", bodyId, TimeSpan.TicksPerHour);
        var local = OrbitalNavigation.ToLocal(lab.CelestialPositions, bodyId, target.AtTicks, target.State);
        var radius = SolarSystem.CreateCatalogue().Bodies[bodyId].RadiusKm + altitude;
        Assert.InRange(Math.Abs(Vector3d.From(local.PositionKm).Length - radius), 0, 1e-6);
        Assert.InRange(Math.Abs(Vector3d.From(local.VelocityKmPerSecond).Length
            - Math.Sqrt(lab.CelestialPositions.GetEnvironment(bodyId).GravitationalParameterKm3PerSecond2 / radius)), 0, 1e-9);
        Assert.Equal(target.State, target.ToAdvisoryDestination().StateAt(lab.CelestialPositions, target.AtTicks));
        var spacecraft = lab.TargetStateAt("spacecraft", SystemLab.ShipId, target.AtTicks);
        Assert.Equal(lab.ShipStateAt(target.AtTicks), spacecraft.State);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoastCrossingIsContinuousAndDeterministicAcrossQueryAdvanceAndRestore(bool exit)
    {
        var catalogue = SolarSystem.CreateCatalogue();
        var earth = SolarSystem.Earth;
        var sphere = catalogue.GetEnvironment(earth).SphereOfInfluenceRadiusKm!.Value;
        var primary = exit ? earth : SolarSystem.Sun;
        var next = exit ? SolarSystem.Sun : earth;
        var local = new CelestialState(new(sphere * (exit ? 0.9 : 1.1), 0, 0), new(exit ? 2 : -2, 1, 0));
        var initial = OrbitalNavigation.ToSystem(catalogue, earth, 0, local);
        MovementExecution Create(bool automatic)
        {
            var world = new MovementExecution(catalogue);
            world.AddSpacecraft("test", initial, primary, automaticPrimary: automatic);
            return world;
        }

        var uninterrupted = Create(true);
        var boundary = Assert.Single(uninterrupted.Advance(TimeSpan.FromDays(30)));
        Assert.Equal("primary-change", boundary.Kind);
        Assert.Equal(next, uninterrupted.ExportState().Spacecraft[0].PrimaryBodyId);
        Assert.InRange(boundary.AtTicks, 1, TimeSpan.TicksPerDay * 29);
        AssertNear(Create(false).StateAt("test", boundary.AtTicks), uninterrupted.StateAt("test", boundary.AtTicks));

        var partitioned = Create(true);
        var future = boundary.AtTicks + TimeSpan.TicksPerMinute;
        var prediction = partitioned.ResolveSpacecraftTarget("test", future);
        Assert.Equal(next, prediction.ReferenceBodyId);
        Assert.Equal(0, partitioned.ElapsedTicks);
        Assert.Empty(partitioned.Advance(TimeSpan.FromTicks(boundary.AtTicks - 1)));
        var restored = MovementExecution.RestoreState(partitioned.ExportState());
        Assert.Equal(boundary, Assert.Single(restored.Advance(TimeSpan.FromSeconds(5))));
        Assert.Equal(boundary, Assert.Single(partitioned.Advance(TimeSpan.FromSeconds(5))));
        Assert.Equal(uninterrupted.ExportState().Spacecraft[0], restored.ExportState().Spacecraft[0]);
        AssertNear(prediction.State, restored.StateAt("test", future));

        var legacy = Create(false).ExportState() with { FormatVersion = 4 };
        Assert.Empty(MovementExecution.RestoreState(legacy).Advance(TimeSpan.FromDays(30)));
    }

    [Fact]
    public void CancellingEarthCenterCourseCanAdvanceWithoutBreakingTheClock()
    {
        var lab = new SystemLab();
        lab.OrderTravel(SolarSystem.Earth);
        lab.Advance(TimeSpan.FromHours(1));
        lab.CancelTravel();
        var before = lab.ElapsedTicks;
        Assert.False(lab.Advance(TimeSpan.FromSeconds(30)));
        Assert.Equal(before + TimeSpan.FromSeconds(30).Ticks, lab.ElapsedTicks);
    }

    private static void AssertNear(CelestialState expected, CelestialState actual)
    {
        Assert.InRange(expected.PositionKm.DistanceTo(actual.PositionKm), 0, 1e-6);
        Assert.InRange((Vector3d.From(expected.VelocityKmPerSecond) - Vector3d.From(actual.VelocityKmPerSecond)).Length, 0, 1e-9);
    }
}
