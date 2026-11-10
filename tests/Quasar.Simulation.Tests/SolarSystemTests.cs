using System.Text.Json.Nodes;

namespace Quasar.Simulation.Tests;

public sealed class SolarSystemTests
{
    [Fact]
    public void CatalogueMatchesEpochAndMovesWithTheClock()
    {
        var lab = new SystemLab();
        var snapshot = lab.Snapshot();
        Assert.Equal(41, snapshot.Objects.Length);
        Assert.Equal(8, snapshot.Objects.Count(body => body.Kind == "planet"));
        Assert.Equal(2, snapshot.Objects.Count(body => body.Kind == "station" && body.ParentId == "planet-earth"));
        Assert.Single(snapshot.Objects, body => body.Kind == "spacecraft");
        var earth = lab.CelestialPositions.GetState("planet-earth", 0);
        var jplEarth = new Position3(102539354.3866291, 107012760.5496008, -6815.366554461420);
        Assert.InRange(earth.PositionKm.DistanceTo(jplEarth), 0, .01);
        Assert.InRange(lab.CelestialPositions.GetEnvironment("moon-moon").PeriodSeconds!.Value / 86400, 25, 29);
        Assert.True(snapshot.Objects.Single(body => body.Id == "comet-halley").Orbit!.InclinationRadians > Math.PI / 2);

        var split = new SystemLab();
        lab.Advance(TimeSpan.FromDays(30));
        for (var i = 0; i < 30; i++) split.Advance(TimeSpan.FromDays(1));
        Assert.Equal(lab.Snapshot().Objects, split.Snapshot().Objects);
        var later = lab.Snapshot().Objects;
        var laterEarth = later.Single(body => body.Id == "planet-earth").Position;
        Assert.NotEqual(earth.PositionKm, laterEarth);
        Assert.InRange(laterEarth.DistanceTo(later.Single(body => body.Id == "station-staryard").Position), 42163.99, 42164.01);
    }

    [Fact]
    public void AutomaticallyChoosesVenusAndRestoresOnEitherSideOfEncounter()
    {
        var lab = new SystemLab();
        const int seconds = 300 * 86400;
        var plan = lab.PreviewTravel("planet-mercury", seconds, 25);
        var flyby = Assert.IsType<FlybyEncounter>(plan.Flyby);
        Assert.Equal("planet-venus", flyby.BodyId);
        Assert.InRange(flyby.ClosestApproachKm, 7051.8, 12000);
        Assert.InRange(plan.RequiredDeltaVKmPerSecond, 15, 18);
        Assert.Throws<ArgumentException>(() => lab.PreviewTravel("planet-mercury", seconds, 1));

        lab.CommitTravel(plan);
        lab = RoundTrip(lab);
        Assert.Equal(flyby.ClosestApproachKm, lab.Snapshot().Trajectory!.Flyby!.ClosestApproachKm);
        Assert.True(lab.Advance(TimeSpan.FromSeconds(seconds + 1)));
        Assert.Equal(plan.Segments[0].EndTicks, lab.ElapsedTicks);
        Assert.Contains("Venus", lab.Snapshot().Events[^1].Message);
        lab = RoundTrip(lab);
        Assert.True(lab.Advance(TimeSpan.FromSeconds(seconds)));
        Assert.Equal(plan.ArrivalTicks, lab.ElapsedTicks);
        Assert.Equal(lab.CelestialPositions.GetState("planet-mercury", lab.ElapsedTicks), lab.ShipStateAt(lab.ElapsedTicks));
    }

    [Fact]
    public void SavedFlybyCannotRestoreWithAnUnsafePeriapsis()
    {
        var lab = new SystemLab();
        lab.CommitTravel(lab.PreviewTravel("planet-mercury", 300 * 86400, 25));
        var state = lab.ExportState();
        var bodies = state.Simulation.Bodies;
        var venus = bodies.Single(body => body.Id == "planet-venus");
        bodies = bodies.Replace(venus, venus with { RadiusKm = 12000 });
        var unsafeState = state with { Simulation = state.Simulation with { Bodies = bodies } };
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(unsafeState));
    }

    [Fact]
    public void DirectCourseMeetsEarthAtItsFuturePosition()
    {
        var lab = new SystemLab();
        var plan = lab.PreviewTravel("planet-earth", 21600, 100);
        Assert.Null(plan.Flyby);
        Assert.Equal(lab.CelestialPositions.GetState("planet-earth", plan.ArrivalTicks), plan.StateAt(plan.ArrivalTicks));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacyHeliosCourseRemainsReadable(int version)
    {
        var lab = SystemLab.CreateLegacyFixture();
        lab.OrderTravel("planet-iris");
        var json = JsonNode.Parse(SimulationStateJson.Serialize(lab.ExportState()))!;
        var simulation = json["simulation"]!;
        simulation["formatVersion"] = version;
        Assert.False(simulation["spacecraft"]![0]!.AsObject().ContainsKey("propulsion"));
        if (version == 1)
        {
            foreach (var body in simulation["bodies"]!.AsArray()) body!.AsObject().Remove("radiusKm");
            simulation["spacecraft"]![0]!["trajectory"]!.AsObject().Remove("flyby");
        }
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Equal(lab.Snapshot().Objects, restored.Snapshot().Objects);
        Assert.Equal(lab.Snapshot().Trajectory!.Points, restored.Snapshot().Trajectory!.Points);
        Assert.Null(restored.Snapshot().Ship.Propulsion);
        Assert.True(lab.Advance(TimeSpan.FromDays(1)));
        Assert.True(restored.Advance(TimeSpan.FromDays(1)));
        Assert.Equal(lab.Snapshot().Events, restored.Snapshot().Events);
    }

    private static SystemLab RoundTrip(SystemLab lab) =>
        SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
}
