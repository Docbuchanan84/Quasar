using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class FlightForecastTests
{
    [Fact]
    public void SailForecastContainsTheTrimAndTheBallisticEarthHandoffs()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        lab.StartSailTrim(SystemLab.SailcraftId, 35, 86400);
        var session = new LabSession(lab);
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var forecast = session.ForecastFlight(SystemLab.SailcraftId, CancellationToken.None);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
        Assert.Equal(session.Snapshot().InstanceId, forecast.InstanceId);
        Assert.Equal(forecast.PathVersion, session.Snapshot().FlightPathVersions![SystemLab.SailcraftId]);
        var path = forecast.Path;
        Assert.Equal(180 * TimeSpan.TicksPerDay, path.EndTicks);
        Assert.Equal(Object(lab, SystemLab.SailcraftId).Position, path.Points[0].PositionKm);
        Assert.Equal(Object(lab, SystemLab.SailcraftId).VelocityKmPerSecond, path.Points[0].VelocityKmPerSecond);
        Assert.Contains(path.Points, point => long.Parse(point.AtTicks) == TimeSpan.TicksPerDay);
        Assert.Equal([SolarSystem.Earth, SolarSystem.Sun], path.GravityTransitions.Take(2).Select(entry => entry.BodyId));
        Assert.All(path.GravityTransitions, transition => Assert.Contains(path.Points, point => point.AtTicks == transition.AtTicks));
        AdvanceTo(lab, long.Parse(path.GravityTransitions[1].AtTicks) + TimeSpan.TicksPerDay);
        Assert.NotEqual(forecast.PathVersion, lab.FlightPathVersions()[SystemLab.SailcraftId]);
        AdvanceTo(lab, path.EndTicks);
        Assert.InRange(Object(lab, SystemLab.SailcraftId).Position.DistanceTo(path.Points[^1].PositionKm), 0, .001);
    }

    [Fact]
    public void RocketForecastUsesCurrentBurnThenResumesCoast()
    {
        var lab = new SystemLab();
        var session = new LabSession(lab);
        var coast = session.ForecastFlight(SystemLab.ShipId, CancellationToken.None);
        lab.StartBurn("prograde", 30);
        var powered = session.ForecastFlight(SystemLab.ShipId, CancellationToken.None);
        var cutoff = TimeSpan.FromSeconds(30).Ticks;
        Assert.NotEqual(coast.PathVersion, powered.PathVersion);
        Assert.True(coast.Path.Points[^1].PositionKm.DistanceTo(powered.Path.Points[^1].PositionKm) > 100);
        var sample = Assert.Single(powered.Path.Points, point => long.Parse(point.AtTicks) == cutoff);
        AdvanceTo(lab, cutoff);
        Assert.Equal(Object(lab, SystemLab.ShipId).Position, sample.PositionKm);
        Assert.NotEqual(powered.PathVersion, lab.FlightPathVersions()[SystemLab.ShipId]);
        Assert.Throws<ArgumentException>(() => session.ForecastFlight("planet-mars", CancellationToken.None));
    }

    [Fact]
    public void ForecastForCommittedCourseIsClippedToTheShipAndArrival()
    {
        var lab = SystemLab.CreateLegacyFixture();
        var plan = lab.PreviewTravel("planet-iris", 21600);
        lab.CommitTravel(plan);
        AdvanceTo(lab, TimeSpan.FromHours(2).Ticks);
        var versioned = lab.CaptureFlightForecast(SystemLab.ShipId);
        var path = versioned.Forecast(CancellationToken.None);
        Assert.Equal(lab.ElapsedTicks, path.StartTicks);
        Assert.Equal(plan.ArrivalTicks, path.EndTicks);
        Assert.All(path.Points, point => Assert.Equal(plan.StateAt(long.Parse(point.AtTicks)).PositionKm, point.PositionKm));
        AdvanceTo(lab, path.EndTicks);
        Assert.NotEqual(versioned.Version, lab.FlightPathVersions()[SystemLab.ShipId]);
    }

    private static LabObject Object(SystemLab lab, string id) => lab.Snapshot().Objects.Single(item => item.Id == id);

    private static void AdvanceTo(SystemLab lab, long ticks)
    {
        while (lab.ElapsedTicks < ticks) lab.Advance(TimeSpan.FromTicks(ticks - lab.ElapsedTicks));
    }
}
