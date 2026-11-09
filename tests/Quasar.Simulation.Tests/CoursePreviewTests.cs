using Quasar.Server;
using Quasar.Simulation;

namespace Quasar.Simulation.Tests;

public class CoursePreviewTests
{
    private static readonly string Client = Guid.NewGuid().ToString();

    [Fact]
    public void PreviewAndDiscardLeaveAllCanonicalStateUntouched()
    {
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        var before = session.Snapshot();
        var preview = session.PreviewCourse("planet-iris", 21600);
        var after = session.Snapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.World.ElapsedTicks, after.World.ElapsedTicks);
        Assert.Equal(before.World.Objects, after.World.Objects);
        Assert.Equal(before.World.Ship, after.World.Ship);
        Assert.Equal(before.World.Events, after.World.Events);
        Assert.Null(after.World.Trajectory);
        Assert.Equal(before.InstanceId, preview.InstanceId);
        Assert.Equal(before.Revision, preview.Revision);
        Assert.True(preview.SampledDistanceKm > 0);
        Assert.NotEmpty(preview.Warnings);
        Assert.Null(preview.Trajectory.Fuel.PropellantMassKg);
    }

    [Fact]
    public void ConfirmationCommitsExactlyReviewedSamplesAndMovesWithoutAnyMap()
    {
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        var preview = session.PreviewCourse("planet-nereid", 21600);
        var ordered = Send(session, "commit-course", preview.PreviewId);
        Assert.Equal(preview.Trajectory.Points, ordered.World.Trajectory!.Points);
        Assert.Equal(preview.Trajectory.RequiredDeltaVKmPerSecond, ordered.World.Trajectory.RequiredDeltaVKmPerSecond);
        foreach (var point in preview.Trajectory.Points)
            Assert.Equal(point.PositionKm, session.ShipStateAt(long.Parse(point.AtTicks)).PositionKm);
        Send(session, "configure"); Send(session, "play"); session.Tick();
        var arrived = session.Snapshot();
        Assert.Equal(long.Parse(preview.Trajectory.ArrivalTicks), arrived.World.ElapsedTicks);
        Assert.Null(arrived.World.Trajectory);
        Assert.False(arrived.Running);
        Assert.Equal(arrived.World.Objects.Single(o => o.Id == "planet-nereid").Position,
            arrived.World.Objects.Single(o => o.Id == SystemLab.ShipId).Position);
    }

    [Fact]
    public void ChangedStateRejectsOldTokenEvenWithLatestCommandRevision()
    {
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        var preview = session.PreviewCourse("planet-iris", 21600);
        Send(session, "configure");
        var before = session.Snapshot();
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", preview.PreviewId));
        Assert.Equal(before.Revision, session.Snapshot().Revision);
        Assert.Null(session.Snapshot().World.Trajectory);
    }

    [Fact]
    public void TokensAreInstanceBoundBoundedAndSingleUse()
    {
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        var first = session.PreviewCourse("planet-iris", 21600);
        Assert.Throws<InvalidOperationException>(() => Send(new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture()), "commit-course", first.PreviewId));
        CoursePreview last = first;
        for (var i = 0; i < 32; i++) last = session.PreviewCourse("planet-iris", 21600);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", first.PreviewId));
        Send(session, "commit-course", last.PreviewId);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", last.PreviewId));
    }

    [Fact]
    public void InvalidInputsAndRunningPreviewDoNotIssueOrders()
    {
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        Assert.Throws<ArgumentException>(() => session.PreviewCourse("missing", 21600));
        Assert.Throws<ArgumentException>(() => session.PreviewCourse("planet-iris", 0));
        Assert.Throws<ArgumentException>(() => session.PreviewCourse("planet-iris", 315576001));
        Send(session, "play");
        Assert.Throws<InvalidOperationException>(() => session.PreviewCourse("planet-iris", 21600));
        Assert.Null(session.Snapshot().World.Trajectory);
        Assert.Single(session.Snapshot().World.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticOptionsCommitExactlyTheSelectedTrajectory(bool chooseLowestCost)
    {
        var session = new LabSession(TimeProvider.System);
        var before = session.Snapshot();
        var options = session.PreviewAutomaticCourse("planet-jupiter", 25, CancellationToken.None);
        var fastest = Assert.IsType<CoursePreview>(options.Fastest);
        var lowestCost = Assert.IsType<CoursePreview>(options.LowestManeuverCost);
        Assert.True(fastest.Trajectory.TravelSeconds < lowestCost.Trajectory.TravelSeconds);
        Assert.True(lowestCost.Trajectory.RequiredDeltaVKmPerSecond < fastest.Trajectory.RequiredDeltaVKmPerSecond);
        Assert.InRange(fastest.Trajectory.RequiredDeltaVKmPerSecond, 0, 25);
        Assert.Equal(before.Revision, session.Snapshot().Revision);
        Assert.Null(session.Snapshot().World.Trajectory);

        var selected = chooseLowestCost ? lowestCost : fastest;
        var other = chooseLowestCost ? fastest : lowestCost;
        var ordered = Send(session, "commit-course", selected.PreviewId);
        Assert.Equal(selected.Trajectory.Points, ordered.World.Trajectory!.Points);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", other.PreviewId));
    }

    [Fact]
    public void AutomaticSearchCanChooseAGravityAssist()
    {
        var session = new LabSession(TimeProvider.System);
        var options = session.PreviewAutomaticCourse("planet-mercury", 25, CancellationToken.None);
        var fastest = Assert.IsType<CoursePreview>(options.Fastest);
        Assert.Equal("planet-venus", fastest.Trajectory.Flyby?.BodyId);
        Assert.InRange(fastest.Trajectory.RequiredDeltaVKmPerSecond, 0, 25);
    }

    [Fact]
    public void NoRouteAndCancellationLeaveTheSessionUntouched()
    {
        var session = new LabSession(TimeProvider.System);
        var before = session.Snapshot();
        var result = session.PreviewAutomaticCourse("planet-jupiter", 1, CancellationToken.None);
        Assert.Null(result.Fastest);
        Assert.Null(result.LowestManeuverCost);
        Assert.Throws<OperationCanceledException>(() =>
            session.PreviewAutomaticCourse("planet-jupiter", 25, new CancellationToken(canceled: true)));
        Assert.Equal(before.Revision, session.Snapshot().Revision);
        Assert.Null(session.Snapshot().World.Trajectory);
    }

    [Fact]
    public void SearchTestsFortyUniqueDurationsAndDeduplicatesTheOnlyFeasiblePlan()
    {
        var sampled = new List<int>();
        var plan = SystemLab.CreateLegacyFixture().PreviewTravel("planet-iris");
        var options = CourseSearch.Find(seconds =>
        {
            sampled.Add(seconds);
            return seconds == 1 ? plan : null;
        }, CancellationToken.None);

        Assert.Equal(40, sampled.Count);
        Assert.Equal(40, sampled.Distinct().Count());
        Assert.Equal(1, sampled[0]);
        Assert.Equal(CourseSearch.MaximumSeconds, sampled[^1]);
        Assert.Same(plan, options!.Fastest);
        Assert.Same(options.Fastest, options.LowestManeuverCost);
    }

    [Fact]
    public void SearchStopsBetweenEvaluationsWhenCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        Assert.Throws<OperationCanceledException>(() => CourseSearch.Find(_ =>
        {
            count++;
            cancellation.Cancel();
            return null;
        }, cancellation.Token));
        Assert.Equal(1, count);
    }

    private static SessionSnapshot Send(LabSession session, string operation, string? previewId = null)
    {
        var state = session.Snapshot();
        return session.Execute(new(state.InstanceId, state.Revision, Client, operation,
            DurationSeconds: 2592000, Mode: "once", PreviewId: previewId));
    }
}
