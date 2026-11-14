using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class SolarSailTests
{
    [Fact]
    public void SailForceDependsOnSunlightAndSignedTrimRatherThanStoredFuel()
    {
        var sail = new SolarSail(110, 30, 12_000);
        var atOneAu = new Vector3d(SolarSail.AstronomicalUnitKm, 0, 0);
        var velocity = new Vector3d(0, 30, 0);
        var faceOn = sail.Acceleration(atOneAu, velocity, 0);
        var outward = sail.Acceleration(atOneAu, velocity, 35);
        var inward = sail.Acceleration(atOneAu, velocity, -35);
        Assert.InRange(faceOn.X * 1000, .00077, .00079);
        Assert.Equal(faceOn.Length / 4, sail.Acceleration(atOneAu * 2, velocity, 0).Length, 15);
        Assert.True(outward.Y > 0);
        Assert.Equal(outward.X, inward.X);
        Assert.Equal(outward.Y, -inward.Y);
        Assert.True(inward.X > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void TrialHasAUsefulFlybyAndSailTrimChangesIt(int startDay)
    {
        var lab = new SystemLab();
        AdvanceTo(lab, TimeSpan.FromDays(startDay).Ticks);
        var rocket = lab.Snapshot().Ship;
        lab.LaunchSailcraft();
        Assert.Equal(rocket, lab.Snapshot().Ship);
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var comparison = lab.PreviewSail(35, 86400);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
        var coast = Assert.Single(comparison.CoastFlybys, flyby => flyby.BodyId == SolarSystem.Earth);
        var trim = Assert.Single(comparison.TrimFlybys, flyby => flyby.BodyId == SolarSystem.Earth);
        Assert.InRange(coast.ClosestAltitudeKm, 13_950, 14_050);
        Assert.True(Math.Abs(coast.SolarSpeedChangeKmPerSecond!.Value) > 1, $"{coast}");
        Assert.True(Math.Abs(trim.ClosestAltitudeKm - coast.ClosestAltitudeKm) > 50, $"{coast} vs {trim}");
        var endpoint = comparison.Trim!.Points[^1];
        lab.StartSailTrim(SystemLab.SailcraftId, 35, 86400);
        AdvanceTo(lab, long.Parse(endpoint.AtTicks));
        var actual = SailObject(lab);
        Assert.InRange(actual.Position.DistanceTo(endpoint.PositionKm), 0, 1e-4);
        Assert.Contains(lab.Snapshot().Events, entry => entry.ObjectId == SystemLab.SailcraftId && entry.Kind == "primary-change"
            && entry.Message.Contains("Earth", StringComparison.Ordinal));
        Assert.Contains(lab.Snapshot().Events, entry => entry.ObjectId == SystemLab.SailcraftId && entry.Kind == "primary-change"
            && entry.Message.Contains("Sun", StringComparison.Ordinal));
        Assert.Equal(lab.Snapshot().Objects, RoundTrip(lab).Snapshot().Objects);
    }

    [Fact]
    public void MidTrimCheckpointAndDifferentClockPartitionsProduceIdenticalStates()
    {
        var direct = new SystemLab();
        direct.LaunchSailcraft();
        direct.StartSailTrim(SystemLab.SailcraftId, -35, 86400);
        var resumed = RoundTrip(direct);
        AdvanceTo(resumed, TimeSpan.FromHours(12).Ticks + 17);
        resumed = RoundTrip(resumed);
        Assert.Equal("Sailing", resumed.Snapshot().SolarSail!.Status);
        Assert.Equal(-35, resumed.Snapshot().SolarSail!.AngleDegrees);
        var end = TimeSpan.FromDays(7).Ticks;
        AdvanceTo(resumed, end);
        AdvanceTo(direct, end);
        Assert.Equal(SimulationStateJson.Serialize(direct.ExportState()), SimulationStateJson.Serialize(resumed.ExportState()));
        Assert.Single(resumed.Snapshot().Events, entry => entry.Kind == "sail-complete");
        Assert.Null(resumed.ExportState().Simulation.Spacecraft.Single(ship => ship.Id == SystemLab.SailcraftId).Propulsion);
    }

    [Fact]
    public void RocketAndSailCanFinishOrdersAtTheSameTickAndRestoreTheirOwnHistories()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        lab.StartBurn("prograde", 30);
        lab.StartSailTrim(SystemLab.SailcraftId, 35, 30);
        var restored = RoundTrip(lab);
        var fuel = restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg;
        Assert.True(restored.Advance(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromSeconds(30).Ticks, restored.ElapsedTicks);
        var world = restored.Snapshot();
        Assert.Equal("burn-complete", world.Events[^2].Kind);
        Assert.Equal("sail-complete", world.Events[^1].Kind);
        Assert.True(world.Ship.Propulsion!.AvailablePropellantKg < fuel);
        Assert.Equal(140, world.SolarSail!.MassKg);
        Assert.Equal(world.Events, RoundTrip(restored).Snapshot().Events);
    }

    [Fact]
    public void InvalidOrExpiredOrdersCannotSteerEitherCraft()
    {
        var lab = new SystemLab();
        var session = new LabSession(lab);
        var client = Guid.NewGuid().ToString();
        SessionSnapshot Send(string operation, string? shipId = null, string? previewId = null)
        {
            var snapshot = session.Snapshot();
            return session.Execute(new(snapshot.InstanceId, snapshot.Revision, client, operation,
                DurationSeconds: 3600, Mode: "once", PreviewId: previewId, ShipId: shipId));
        }
        Send("launch-sailcraft");
        Assert.Throws<InvalidOperationException>(() => Send("launch-sailcraft"));
        Assert.Throws<ArgumentException>(() => Send("start-burn", SystemLab.SailcraftId));
        Assert.Throws<ArgumentException>(() => session.PreviewSail(double.NaN, 3600));
        Assert.Throws<InvalidOperationException>(() => session.PreviewSail(35, 5 * 86400));
        var first = Token();
        Send("configure");
        Assert.Throws<InvalidOperationException>(() => Send("start-sail-trim", SystemLab.SailcraftId, first));
        var current = Token();
        Assert.Throws<ArgumentException>(() => Send("start-sail-trim", SystemLab.ShipId, current));
        Assert.Equal("Sailing", Send("start-sail-trim", SystemLab.SailcraftId, current).World.SolarSail!.Status);
        Assert.Equal("Coasting", Send("feather-sail", SystemLab.SailcraftId).World.SolarSail!.Status);
        Assert.Equal("Coasting", RoundTrip(lab).Snapshot().SolarSail!.Status);

        string Token() => session.PreviewSail(35, 3600).PreviewId!;
    }

    [Fact]
    public void OldCheckpointKeepsItsShipAndCanOptInToTheTrial()
    {
        var saved = new SystemLab().ExportState();
        var versionSeven = saved with { Simulation = saved.Simulation with { FormatVersion = 7 } };
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(versionSeven)));
        Assert.Single(restored.ExportState().Simulation.Spacecraft);
        Assert.True(restored.CanLaunchSailcraft);
        restored.LaunchSailcraft();
        var trial = restored.ExportState();
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(
            trial with { Simulation = trial.Simulation with { FormatVersion = 7 } }));
        Assert.Equal(trial.Simulation.Spacecraft[0], RoundTrip(restored).ExportState().Simulation.Spacecraft[0]);
    }

    private static LabObject SailObject(SystemLab lab) => lab.Snapshot().Objects.Single(ship => ship.Id == SystemLab.SailcraftId);
    private static SystemLab RoundTrip(SystemLab lab) =>
        SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));

    private static void AdvanceTo(SystemLab lab, long ticks)
    {
        while (lab.ElapsedTicks < ticks)
            lab.Advance(TimeSpan.FromTicks(ticks - lab.ElapsedTicks));
    }
}
