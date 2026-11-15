using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class FissionElectricTests
{
    [Fact]
    public void SurveyorPlotsAValidMultiDayMarsApproach()
    {
        var lab = new SystemLab();
        lab.LaunchSurveyor();
        var result = lab.CaptureDestinationOrder(new(SystemLab.SurveyorId, "planet-mars", "approach"))(CancellationToken.None);
        Assert.True(result.Fastest is not null, string.Join(" | ", result.Reasons));
        var plan = result.Fastest.Trajectory;
        Assert.InRange(plan.TravelSeconds, 1, TimeSpan.FromDays(365).TotalSeconds);
        Assert.True(plan.BurnSchedule!.Sum(burn => burn.DurationSeconds) > 2 * 86400);
        Assert.InRange(plan.Arrival!.PositionErrorKm, 0, .01);
        Assert.InRange(plan.Arrival.VelocityErrorKmPerSecond, 0, 1e-6);
        Assert.InRange(plan.Fuel.PropellantMassKg!.Value, 0, 6000);
    }

    [Fact]
    public void SurveyorFindsTheVenusWindowAtALaterLaunchEpoch()
    {
        var lab = new SystemLab();
        var launch = TimeSpan.FromDays(4395).Ticks;
        while (lab.ElapsedTicks < launch)
            lab.Advance(TimeSpan.FromTicks(launch - lab.ElapsedTicks));
        lab.LaunchSurveyor();

        var result = lab.CaptureDestinationOrder(new(SystemLab.SurveyorId, "planet-venus", "approach"))(CancellationToken.None);

        Assert.True(result.Fastest is not null, string.Join(" | ", result.Reasons));
        var trajectory = result.Fastest.Trajectory;
        Assert.InRange(trajectory.TravelSeconds, TimeSpan.FromDays(220).TotalSeconds, TimeSpan.FromDays(245).TotalSeconds);
        Assert.InRange(trajectory.Fuel.PropellantMassKg!.Value, 0, 6000);
        Assert.InRange(trajectory.Arrival!.PositionErrorKm, 0, .01);
        Assert.InRange(trajectory.Arrival.VelocityErrorKmPerSecond, 0, 1e-6);
    }

    [Fact]
    public void SurveyorCanCoastUntilTheMarsWindowAndFinishAfterReload()
    {
        var lab = new SystemLab();
        var start = TimeSpan.FromDays(4395).Ticks;
        AdvanceTo(start);
        lab.LaunchSurveyor();
        var preview = lab.CaptureDestinationOrder(new(SystemLab.SurveyorId, "planet-mars", "approach"))(CancellationToken.None);
        var candidate = preview.Efficient;
        Assert.True(candidate is not null, string.Join(" | ", preview.Reasons));
        var course = candidate.Trajectory;
        var ignition = long.Parse(course.BurnSchedule![0].StartTicks);
        Assert.Equal(start, long.Parse(course.DepartureTicks));
        Assert.True(ignition > start + TimeSpan.TicksPerDay);
        Assert.InRange(course.Fuel.PropellantMassKg!.Value, 0, 6000);
        candidate.Commit();

        AdvanceTo(ignition - TimeSpan.TicksPerDay);
        var saved = lab.ExportState();
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(saved with
        {
            Simulation = saved.Simulation with { FormatVersion = 11 }
        }));
        lab = Restore(lab);
        var waiting = lab.Snapshot().Objects.Single(body => body.Id == SystemLab.SurveyorId);
        Assert.Equal(6000, waiting.ShipStatus!.Propulsion!.AvailablePropellantKg);
        var arrival = long.Parse(course.ArrivalTicks);
        AdvanceTo(arrival);
        var final = lab.Snapshot();
        Assert.Contains(final.Events, entry => entry is { Kind: "course-approach-complete", ObjectId: SystemLab.SurveyorId }
            && entry.AtTicks == arrival);
        var arrived = final.Objects.Single(body => body.Id == SystemLab.SurveyorId);
        Assert.InRange(arrived.Position.DistanceTo(course.Points[^1].PositionKm), 0, 1e-6);

        void AdvanceTo(long ticks)
        {
            while (lab.ElapsedTicks < ticks)
                lab.Advance(TimeSpan.FromTicks(ticks - lab.ElapsedTicks));
        }
    }

    [Fact]
    public void SurveyorCommitsRestoresAndFliesTheReviewedMarsCourse()
    {
        var lab = new SystemLab();
        lab.LaunchSailcraft();
        var session = new LabSession(lab);
        var client = Guid.NewGuid().ToString();
        SessionSnapshot Command(string operation, string? previewId = null, int? duration = null, string? mode = null)
        {
            var current = session.Snapshot();
            return session.Execute(new(current.InstanceId, current.Revision, client, operation, duration, mode,
                PreviewId: previewId, ShipId: SystemLab.SurveyorId));
        }
        Command("launch-surveyor");
        var before = lab.ExportState();
        Assert.Equal(3, before.Simulation.Spacecraft.Length);
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(before with
        {
            Simulation = before.Simulation with { FormatVersion = 9 }
        }));
        var preview = session.PreviewDestination(new(SystemLab.SurveyorId, "planet-mars", "approach"), CancellationToken.None);
        var route = preview.Fastest!;
        Assert.NotNull(preview.LowestManeuverCost);
        Assert.NotNull(preview.MostDirect);
        Assert.Equal(SimulationStateJson.Serialize(before), SimulationStateJson.Serialize(lab.ExportState()));
        var underway = Command("commit-course", route.PreviewId);
        var status = underway.World.Objects.Single(body => body.Id == SystemLab.SurveyorId).ShipStatus!;
        Assert.Equal("planet-mars", status.DestinationId);
        Assert.Equal(300, status.ElectricDrive!.ThrustN);
        Assert.Equal(6000, status.Propulsion!.AvailablePropellantKg);

        Command("configure", duration: 86400, mode: "once");
        Command("play");
        session.Tick();
        var mid = lab.ExportState();
        var storedShip = mid.Simulation.Spacecraft.Single(ship => ship.Id == SystemLab.SurveyorId);
        Assert.NotNull(storedShip.FiniteCourse);
        Assert.NotNull(storedShip.Propulsion!.ElectricDrive);
        Assert.Null(storedShip.Propulsion.Engine);
        Assert.True(storedShip.Propulsion.Tank.QuantityKg < 6000);
        var restored = Restore(lab);
        Assert.Equal(lab.Snapshot().Objects, restored.Snapshot().Objects);
        Assert.Equal(lab.Snapshot().Events, restored.Snapshot().Events);

        session = new LabSession(restored);
        Command("configure", duration: 2592000, mode: "continuous");
        Command("play");
        for (var count = 0; count < 120 && session.Snapshot().Running; count++) session.Tick();
        var arrived = session.Snapshot();
        Assert.False(arrived.Running);
        Assert.Equal(SystemLab.SurveyorId, arrived.Interrupt?.ObjectId);
        Assert.Equal("course-approach-complete", arrived.Interrupt?.Kind);
        Assert.Equal(long.Parse(route.Trajectory.ArrivalTicks), arrived.World.ElapsedTicks);
        var end = arrived.World.Objects.Single(body => body.Id == SystemLab.SurveyorId);
        Assert.InRange(end.Position.DistanceTo(route.Trajectory.Points[^1].PositionKm), 0, 1e-6);
        Assert.Equal(6000 - route.Trajectory.Fuel.PropellantMassKg!.Value, end.ShipStatus!.Propulsion!.AvailablePropellantKg, 6);
        Assert.Equal(20_000, arrived.World.Ship.Propulsion!.AvailablePropellantKg);
        Assert.Equal(arrived.World.Objects, Restore(restored).Snapshot().Objects);
    }

    [Theory]
    [InlineData(149_597_870.7, 1.32712440018e11)]
    [InlineData(66_000, 398600.435)]
    public void ElectricIntegratorAgreesWithOneSecondReferenceForAFullDay(double radiusKm, double gravity)
    {
        var initial = new CelestialState(new(radiusKm, 0, 0), new(0, Math.Sqrt(gravity / radiusKm), 0));
        var tank = new PropellantTank(1000, 6000, 6000, "argon");
        var electric = new SpacecraftPropulsion(9000, null, tank, new(4000, 7, 2, 10, .6, 40_000, "argon"));
        var reference = new SpacecraftPropulsion(9000, new(4000, 7, 2, 300, 40_000, "argon"), tank);
        var direction = new Velocity3(0, 1, 0);
        var through = TimeSpan.TicksPerDay;
        var fast = new PoweredTrajectory(initial, direction, gravity, electric, 0, through).StateAt(through);
        var slow = new PoweredTrajectory(initial, direction, gravity, reference, 0, through).StateAt(through);
        Assert.InRange(fast.PositionKm.DistanceTo(slow.PositionKm), 0, .0001);
        Assert.InRange((Vector3d.From(fast.VelocityKmPerSecond) - Vector3d.From(slow.VelocityKmPerSecond)).Length, 0, 1e-8);
    }

    private static SystemLab Restore(SystemLab lab) =>
        SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
}
