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

    [Theory]
    [InlineData(-0.01, true)]
    [InlineData(0, false)]
    [InlineData(0.01, false)]
    public void ResolvesBriefFlyThroughAndRejectsGrazingContacts(double offsetKm, bool enters)
    {
        var catalogue = SolarSystem.CreateCatalogue();
        var initial = EarthFlyby(catalogue, offsetKm);
        MovementExecution Create(bool automatic)
        {
            var movement = new MovementExecution(catalogue);
            movement.AddSpacecraft("test", initial, SolarSystem.Sun, automaticPrimary: automatic);
            return movement;
        }

        var unchanged = Create(false);
        var radius = catalogue.GetEnvironment(SolarSystem.Earth).SphereOfInfluenceRadiusKm!.Value * .99;
        double Distance(int seconds) => unchanged.StateAt("test", seconds * TimeSpan.TicksPerSecond)
            .PositionKm.DistanceTo(catalogue.GetState(SolarSystem.Earth, seconds * TimeSpan.TicksPerSecond).PositionKm);
        Assert.True(Distance(0) > radius && Distance(60) > radius);
        if (enters) Assert.True(Distance(30) < radius);

        const long end = 10 * TimeSpan.TicksPerMinute;
        var large = Create(true);
        var events = large.Advance(TimeSpan.FromMinutes(1));
        if (enters)
        {
            var entry = Assert.Single(events);
            Assert.InRange(entry.AtTicks, 29 * TimeSpan.TicksPerSecond, 30 * TimeSpan.TicksPerSecond);
            AssertNear(unchanged.StateAt("test", entry.AtTicks), large.StateAt("test", entry.AtTicks));
            Assert.Equal(SolarSystem.Earth, large.PrimaryOf("test"));
            Assert.Equal("primary-change", Assert.Single(large.Advance(TimeSpan.FromTicks(end - large.ElapsedTicks))).Kind);
            Assert.Equal(SolarSystem.Sun, large.PrimaryOf("test"));
        }
        else Assert.Empty(events);

        Assert.Empty(large.Advance(TimeSpan.FromTicks(end - large.ElapsedTicks)));
        var small = Create(true);
        while (small.ElapsedTicks < end)
            small.Advance(TimeSpan.FromTicks(Math.Min(7 * TimeSpan.TicksPerSecond, end - small.ElapsedTicks)));
        Assert.Equal(large.ExportState().Spacecraft[0], small.ExportState().Spacecraft[0]);
        AssertNear(large.StateAt("test", large.ElapsedTicks), small.StateAt("test", small.ElapsedTicks));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.00002, true)]
    public void DistinguishesGrazingApoapsisFromABriefExit(double offsetKm, bool exits)
    {
        var catalogue = SolarSystem.CreateCatalogue();
        var earth = SolarSystem.Earth;
        const long midpoint = 30 * TimeSpan.TicksPerSecond;
        var exitRadius = catalogue.GetEnvironment(earth).SphereOfInfluenceRadiusKm!.Value * 1.01;
        var radius = exitRadius + offsetKm;
        var mu = catalogue.GetEnvironment(earth).GravitationalParameterKm3PerSecond2;
        var coast = new TwoBodyTrajectory(new(new(radius, 0, 0), new(0, .7 * Math.Sqrt(mu / radius), 0)), midpoint, mu);
        Assert.True(Vector3d.From(coast.StateAt(0).PositionKm).Length < exitRadius);
        Assert.True(Vector3d.From(coast.StateAt(2 * midpoint).PositionKm).Length < exitRadius);
        var movement = new MovementExecution(catalogue);
        movement.AddSpacecraft("test", OrbitalNavigation.ToSystem(catalogue, earth, 0, coast.StateAt(0)), earth,
            automaticPrimary: true);
        var events = movement.Advance(TimeSpan.FromMinutes(1));
        if (exits)
        {
            Assert.InRange(Assert.Single(events).AtTicks, 1, midpoint);
            Assert.Equal(SolarSystem.Sun, movement.PrimaryOf("test"));
        }
        else Assert.Empty(events);
    }

    [Theory]
    [InlineData(1, "a-earth")]
    [InlineData(1.001, SolarSystem.Earth)]
    public void OrdersOverlappingSpheresByFirstCrossingThenBodyIdAndProcessesAllShips(double earthMassFactor, string expectedPrimary)
    {
        var solar = SolarSystem.CreateCatalogue();
        var earth = solar.Bodies[SolarSystem.Earth];
        var catalogue = new CelestialPositionService(solar.Bodies.Values
            .Where(body => body.Id != earth.Id)
            .Append(earth with { Id = "a-earth" })
            .Append(earth with { GravitationalParameterKm3PerSecond2 = earth.GravitationalParameterKm3PerSecond2 * earthMassFactor }));
        var initial = EarthFlyby(catalogue, -.01);
        var movement = new MovementExecution(catalogue);
        movement.AddSpacecraft("z-ship", initial, SolarSystem.Sun, automaticPrimary: true);
        movement.AddSpacecraft("a-ship", initial, SolarSystem.Sun, automaticPrimary: true);
        var events = movement.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new[] { "a-ship", "z-ship" }, events.Select(e => e.SpacecraftId));
        Assert.All(events, e =>
        {
            Assert.Equal(movement.ElapsedTicks, e.AtTicks);
            Assert.Equal("primary-change", e.Kind);
            Assert.Equal(expectedPrimary, movement.PrimaryOf(e.SpacecraftId));
        });
    }

    [Fact]
    public void QueuedArrivalAndAutomaticEntryAtTheSameTickBothInterrupt()
    {
        var catalogue = SolarSystem.CreateCatalogue();
        var initial = EarthFlyby(catalogue, -.01);
        var movement = new MovementExecution(catalogue);
        movement.AddSpacecraft("coast", initial, SolarSystem.Sun, automaticPrimary: true);
        var probe = MovementExecution.RestoreState(movement.ExportState());
        var at = Assert.Single(probe.Advance(TimeSpan.FromMinutes(1))).AtTicks;

        const double moonMu = 4902.8;
        var orbit = new CelestialState(new(0, 2000, 0), new(0, 0, Math.Sqrt(moonMu / 2000)));
        const string moon = "moon-moon";
        var departure = OrbitalNavigation.ToSystem(catalogue, moon, 0, orbit);
        var target = new TwoBodyTrajectory(orbit, 0, moonMu).StateAt(at);
        movement.AddSpacecraft("arrival", departure, moon);
        var plan = new TrajectoryPlanner(catalogue).Plan(new(departure, 0,
            new(moon, at, new(null, OrbitalNavigation.ToSystem(catalogue, moon, at, target), at))));
        movement.Commit("arrival", plan);

        var events = movement.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new MovementEvent[] { new(at, "arrival", "arrival"), new(at, "coast", "primary-change") }, events);
    }

    [Fact]
    public void AutomaticTargetPredictionHasAnInclusiveThirtyDayHorizonWithoutChangingTheWorld()
    {
        var lab = new SystemLab();
        lab.Advance(TimeSpan.FromSeconds(1));
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var horizon = lab.ElapsedTicks + 30 * TimeSpan.TicksPerDay;
        Assert.Equal(horizon, lab.TargetStateAt("spacecraft", SystemLab.ShipId, horizon).AtTicks);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            lab.TargetStateAt("spacecraft", SystemLab.ShipId, horizon + 1));
        Assert.Contains("up to 30 days", error.Message);
        Assert.Equal(horizon + 1, lab.TargetStateAt("orbit", SolarSystem.Earth, horizon + 1).AtTicks);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
    }

    private static CelestialState EarthFlyby(CelestialPositionService catalogue, double offsetKm)
    {
        const long midpoint = 30 * TimeSpan.TicksPerSecond;
        var radius = catalogue.GetEnvironment(SolarSystem.Earth).SphereOfInfluenceRadiusKm!.Value * .99;
        var midpointState = OrbitalNavigation.ToSystem(catalogue, SolarSystem.Earth, midpoint,
            new(new(0, 0, radius + offsetKm), new(500, 0, 0)));
        return new TwoBodyTrajectory(midpointState, midpoint,
            catalogue.GetEnvironment(SolarSystem.Sun).GravitationalParameterKm3PerSecond2).StateAt(0);
    }

    private static void AssertNear(CelestialState expected, CelestialState actual)
    {
        Assert.InRange(expected.PositionKm.DistanceTo(actual.PositionKm), 0, 1e-6);
        Assert.InRange((Vector3d.From(expected.VelocityKmPerSecond) - Vector3d.From(actual.VelocityKmPerSecond)).Length, 0, 1e-9);
    }
}
