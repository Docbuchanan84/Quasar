using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class TorchCourseTests
{
    [Fact]
    public void TorchRefitPreservesTheCoastAndRoundTripsWithFullReactionMass()
    {
        var lab = new SystemLab();
        lab.Advance(TimeSpan.FromHours(2));
        var before = lab.ShipStateAt(lab.ElapsedTicks);
        var at = lab.ElapsedTicks;
        lab.FitExplorationTorch();

        Assert.Equal(at, lab.ElapsedTicks);
        Assert.Equal(before, lab.ShipStateAt(at));
        var performance = lab.Snapshot().Ship.Propulsion!;
        Assert.Equal(20_000, performance.AvailablePropellantKg);
        Assert.Equal(10, performance.InitialAccelerationMPerSecond2);
        Assert.Equal(0.3, performance.RatedMassFlowKgPerSecond);
        Assert.Equal(1000 * Math.Log(3), performance.TheoreticalDeltaVMPerSecond / 1000, 8);
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
        Assert.Equal(SimulationStateJson.Serialize(lab.ExportState()), SimulationStateJson.Serialize(restored.ExportState()));
    }

    [Theory]
    [InlineData("planet-venus")]
    [InlineData("planet-mars")]
    [InlineData("planet-mercury")]
    public void TorchFindsAndExecutesAnInterplanetaryApproach(string destination)
    {
        var lab = new SystemLab();
        lab.Advance(TimeSpan.FromHours(2));
        lab.FitExplorationTorch();
        var result = lab.CaptureCourseSearch(destination)(CancellationToken.None);
        var plan = Assert.IsType<CourseChoices>(result.Choices).Fastest;
        Assert.True(plan.RequiredDeltaVKmPerSecond > 25);
        Assert.Contains(SystemLab.Present(plan).BurnSchedule!, burn => burn.DurationSeconds > 3600);
        Assert.Equal("approach", plan.Finite!.Arrival.Kind);
        lab.CommitTravel(plan);
        Assert.Throws<InvalidOperationException>(() => lab.FitExplorationTorch());

        var longBurn = SystemLab.Present(plan).BurnSchedule!.First(burn => burn.DurationSeconds > 3600);
        var midburn = long.Parse(longBurn.StartTicks) + (long)(longBurn.DurationSeconds / 2 * TimeSpan.TicksPerSecond);
        while (lab.ElapsedTicks < midburn) lab.Advance(TimeSpan.FromTicks(midburn - lab.ElapsedTicks));
        var checkpoint = SimulationStateJson.Serialize(lab.ExportState());
        var cancelled = SystemLab.RestoreState(SimulationStateJson.Deserialize(checkpoint));
        var beforeCancel = cancelled.ShipStateAt(midburn);
        var remaining = cancelled.Snapshot().Ship.Propulsion;
        cancelled.CancelTravel();
        Assert.Equal(beforeCancel, cancelled.ShipStateAt(midburn));
        Assert.Equal(remaining, cancelled.Snapshot().Ship.Propulsion);
        lab = SystemLab.RestoreState(SimulationStateJson.Deserialize(checkpoint));

        while (lab.ElapsedTicks < plan.ArrivalTicks) lab.Advance(TimeSpan.FromTicks(plan.ArrivalTicks - lab.ElapsedTicks));
        Assert.Equal("course-approach-complete", lab.LastEventKind);
        Assert.Equal(plan.StateAt(plan.ArrivalTicks), lab.ShipStateAt(plan.ArrivalTicks));
        Assert.Equal(20_000 - plan.Fuel.PropellantMassKg!.Value, lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 7);
        var restored = SystemLab.RestoreState(lab.ExportState());
        Assert.Equal(lab.Snapshot().Ship.Propulsion, restored.Snapshot().Ship.Propulsion);
    }

    [Fact]
    public void RefitRequiresPauseAndInvalidatesOldPreviewTokens()
    {
        var session = new LabSession();
        var preview = session.PreviewAutomaticCourse("planet-earth", null, CancellationToken.None).Fastest!;
        var version = session.Snapshot();
        var client = Guid.NewGuid().ToString();
        var upgraded = session.Execute(new(version.InstanceId, version.Revision, client, "fit-exploration-torch"));
        Assert.Throws<InvalidOperationException>(() => session.Execute(new(upgraded.InstanceId, upgraded.Revision, client,
            "commit-course", PreviewId: preview.PreviewId)));
        var running = session.Execute(new(upgraded.InstanceId, upgraded.Revision, client, "play"));
        Assert.Throws<InvalidOperationException>(() => session.Execute(new(running.InstanceId, running.Revision, client, "fit-exploration-torch")));
    }

    [Fact]
    public void LinkedControlsPreserveAReserveAndAdjustUnlockedTravelTime()
    {
        var lab = new SystemLab();
        lab.Advance(TimeSpan.FromHours(2));
        lab.FitExplorationTorch();
        var baseline = lab.CaptureCourseSearch("planet-mars")(CancellationToken.None).Choices!.Fastest;
        var requested = (int)baseline.TravelSeconds;
        var tuned = lab.CaptureCourseSearch("planet-mars", preferredFlightSeconds: requested,
            reservePercent: 95)(CancellationToken.None).Choices!.Tuned!;
        Assert.True(tuned.TravelSeconds > requested);
        Assert.InRange(tuned.Fuel.PropellantMassKg!.Value, 0, 1000);
        Assert.InRange(tuned.Finite!.Arrival.PositionErrorKm, 0, FiniteCourse.PositionToleranceKm);

        var conflict = lab.CaptureCourseSearch("planet-mars", preferredFlightSeconds: requested,
            reservePercent: 95, lockFlightTime: true)(CancellationToken.None);
        Assert.Null(conflict.Choices);
        Assert.Contains(conflict.InfeasibleReasons, reason => reason.Contains("locked travel time"));

        var exact = lab.CaptureCourseSearch("planet-mars", preferredFlightSeconds: requested,
            lockFlightTime: true)(CancellationToken.None).Choices!.Tuned!;
        Assert.Equal(requested, exact.TravelSeconds);
        Assert.Equal(baseline.RequiredDeltaVKmPerSecond, exact.RequiredDeltaVKmPerSecond);
    }
}
