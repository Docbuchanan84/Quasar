using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class DestinationOrderTests
{
    [Fact]
    public void SailAndRocketUseTheSameDestinationOrderContract()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        var session = new LabSession(lab);
        var initial = session.Snapshot();
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var sail = session.PreviewDestination(new(SystemLab.SailcraftId, SolarSystem.Earth, "flyby"), CancellationToken.None);
        Assert.NotNull(sail.Fastest);
        Assert.NotNull(sail.LowestManeuverCost);
        Assert.NotNull(sail.MostDirect);
        Assert.Equal("flyby", sail.Fastest.Trajectory.ArrivalKind);
        Assert.NotEmpty(sail.Fastest.Trajectory.SailSchedule!);
        Assert.Empty(sail.LowestManeuverCost.Trajectory.SailSchedule!);
        Assert.True(sail.Fastest.Trajectory.TravelSeconds < sail.LowestManeuverCost.Trajectory.TravelSeconds);
        Assert.Equal(0, sail.LowestManeuverCost.Trajectory.Fuel.PropellantMassKg);
        var rocket = session.PreviewDestination(new(SystemLab.ShipId, SolarSystem.Earth, "approach"), CancellationToken.None);
        Assert.NotNull(rocket.Fastest);
        Assert.NotNull(rocket.MostDirect);
        Assert.NotEmpty(rocket.Fastest.Trajectory.BurnSchedule!);
        Assert.True(rocket.Fastest.Trajectory.Fuel.PropellantMassKg > 0);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));

        var command = new LabCommand(initial.InstanceId, initial.Revision, Guid.NewGuid().ToString(),
            "commit-course", PreviewId: sail.Fastest.PreviewId, ShipId: SystemLab.ShipId);
        Assert.Throws<ArgumentException>(() => session.Execute(command));
        var issued = session.Execute(command with { ShipId = SystemLab.SailcraftId });
        Assert.Equal("Underway", issued.World.SolarSail!.Status);
        Assert.Equal(SolarSystem.Earth, issued.World.SolarSail.DestinationId);
        Assert.Throws<InvalidOperationException>(() => session.Execute(command with
        { Revision = issued.Revision, ShipId = SystemLab.SailcraftId }));
    }

    [Fact]
    public void ConfirmedFlybyRestoresMidCourseAndPausesAtTheVerifiedPosition()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        var routes = lab.CaptureDestinationOrder(new(SystemLab.SailcraftId, SolarSystem.Earth, "flyby"))(CancellationToken.None);
        var course = routes.Fastest!;
        course.Commit();
        var eta = long.Parse(course.Trajectory.ArrivalTicks);
        var forecast = lab.CaptureFlightForecast(SystemLab.SailcraftId).Forecast(CancellationToken.None);
        Assert.Equal(eta, forecast.EndTicks);
        var end = course.Trajectory.Points[^1];
        Assert.Equal(end, forecast.Points[^1]);
        lab.Advance(TimeSpan.FromHours(1));
        var restored = Restore(lab);
        Assert.Equal(eta, restored.Snapshot().SolarSail!.ArrivalTicks);
        var session = new LabSession(restored);
        var initial = session.Snapshot();
        var client = Guid.NewGuid().ToString();
        var configured = session.Execute(new(initial.InstanceId, initial.Revision, client, "configure", 1296000, "once"));
        session.Execute(new(configured.InstanceId, configured.Revision, client, "play"));
        session.Tick();
        var arrived = session.Snapshot();
        Assert.False(arrived.Running);
        Assert.Equal(eta, arrived.World.ElapsedTicks);
        Assert.Equal("sail-flyby", arrived.Interrupt?.Kind);
        Assert.Equal(end.PositionKm, arrived.World.Objects.Single(item => item.Id == SystemLab.SailcraftId).Position);
        Assert.Equal("Coasting", Restore(restored).Snapshot().SolarSail!.Status);
    }

    [Fact]
    public void SailCourseCancellationPreservesMomentumAndClearsTheOrder()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        var route = lab.CaptureDestinationOrder(new(SystemLab.SailcraftId, SolarSystem.Earth, "flyby"))(CancellationToken.None).Fastest!;
        route.Commit();
        lab.Advance(TimeSpan.FromMinutes(30));
        var state = lab.TargetStateAt("spacecraft", SystemLab.SailcraftId, lab.ElapsedTicks).State;
        lab.CancelCourse(SystemLab.SailcraftId);
        var restored = Restore(lab);
        Assert.Null(restored.Snapshot().SolarSail!.DestinationId);
        var actual = restored.TargetStateAt("spacecraft", SystemLab.SailcraftId, restored.ElapsedTicks).State;
        Assert.InRange(state.PositionKm.DistanceTo(actual.PositionKm), 0, 1e-6);
        Assert.Equal(state.VelocityKmPerSecond, actual.VelocityKmPerSecond);
    }

    [Fact]
    public void SailSearchCanScheduleTwoTrimsSeparatedByCoasting()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        var movement = MovementExecution.RestoreState(lab.ExportState().Simulation);
        var candidates = movement.CaptureSailCourseSearch(SystemLab.SailcraftId, SolarSystem.Earth)(CancellationToken.None);
        var course = Assert.Single(candidates.Where(route => route.State.Maneuvers.Length == 2).Take(1));
        Assert.True(course.State.Maneuvers[1].OffsetSeconds > course.State.Maneuvers[0].DurationSeconds);
        movement.CommitSailCourse(SystemLab.SailcraftId, course);
        var boundaries = new List<long>();
        while (movement.ElapsedTicks < course.ArrivalTicks)
            boundaries.AddRange(movement.Advance(TimeSpan.FromTicks(course.ArrivalTicks - movement.ElapsedTicks)).Select(eventInfo => eventInfo.AtTicks));
        Assert.Contains(TimeSpan.FromSeconds(course.State.Maneuvers[1].OffsetSeconds).Ticks, boundaries);
        Assert.Equal(course.ArrivalTicks, boundaries[^1]);
    }

    private static SystemLab Restore(SystemLab lab) =>
        SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
}
