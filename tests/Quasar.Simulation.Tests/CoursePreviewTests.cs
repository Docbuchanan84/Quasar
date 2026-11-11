using Quasar.Server;
using Quasar.Simulation;

namespace Quasar.Simulation.Tests;

public class CoursePreviewTests
{
    private static readonly string Client = Guid.NewGuid().ToString();

    [Fact]
    public void PreviewAndDiscardLeaveAllCanonicalStateUntouched()
    {
        var session = new LabSession(SystemLab.CreateLegacyFixture());
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
        var session = new LabSession(SystemLab.CreateLegacyFixture());
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
        var session = new LabSession(SystemLab.CreateLegacyFixture());
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
        var session = new LabSession(SystemLab.CreateLegacyFixture());
        var first = session.PreviewCourse("planet-iris", 21600);
        Assert.Throws<InvalidOperationException>(() => Send(new LabSession(SystemLab.CreateLegacyFixture()), "commit-course", first.PreviewId));
        CoursePreview last = first;
        for (var i = 0; i < 32; i++) last = session.PreviewCourse("planet-iris", 21600);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", first.PreviewId));
        Send(session, "commit-course", last.PreviewId);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", last.PreviewId));
    }

    [Fact]
    public void InvalidInputsAndRunningPreviewDoNotIssueOrders()
    {
        var session = new LabSession(SystemLab.CreateLegacyFixture());
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
        var session = new LabSession(SystemLab.CreateChemicalFixture());
        var before = session.Snapshot();
        var options = session.PreviewAutomaticCourse("planet-earth", 25, CancellationToken.None);
        var fastest = Assert.IsType<CoursePreview>(options.Fastest);
        var lowestCost = Assert.IsType<CoursePreview>(options.LowestManeuverCost);
        Assert.True(fastest.Trajectory.TravelSeconds < lowestCost.Trajectory.TravelSeconds);
        Assert.True(lowestCost.Trajectory.RequiredDeltaVKmPerSecond < fastest.Trajectory.RequiredDeltaVKmPerSecond);
        Assert.InRange(fastest.Trajectory.RequiredDeltaVKmPerSecond, 0, 25);
        Assert.True(fastest.Trajectory.Fuel.PropellantMassKg > lowestCost.Trajectory.Fuel.PropellantMassKg);
        Assert.Equal("approach", fastest.Trajectory.Arrival!.Kind);
        Assert.Equal(10.141, fastest.Trajectory.BrakingMarginKg!.Value, 3);
        Assert.Equal(0.1014, fastest.Trajectory.BrakingMarginPercent!.Value, 4);
        Assert.True(fastest.Trajectory.CriticalBrakingReserve);
        Assert.False(lowestCost.Trajectory.CriticalBrakingReserve);
        Assert.Equal(2, fastest.Trajectory.BurnSchedule!.Length);
        Assert.Equal(before.Revision, session.Snapshot().Revision);
        Assert.Null(session.Snapshot().World.Trajectory);

        var selected = chooseLowestCost ? lowestCost : fastest;
        var other = chooseLowestCost ? fastest : lowestCost;
        var ordered = Send(session, "commit-course", selected.PreviewId);
        Assert.Equal(selected.Trajectory.Points, ordered.World.Trajectory!.Points);
        Assert.Throws<InvalidOperationException>(() => Send(session, "commit-course", other.PreviewId));
    }

    [Fact]
    public void AutomaticSearchReportsUnsupportedArrivalWithoutAnOrderToken()
    {
        var session = new LabSession();
        var options = session.PreviewAutomaticCourse("station-staryard", 25, CancellationToken.None, "orbit");
        Assert.Null(options.Fastest);
        Assert.Contains("no modeled gravity", Assert.Single(options.InfeasibleReasons));
    }

    [Fact]
    public void ContinuousStopsExactlyAtEachFiniteBoundaryAndApproachFinishesOutsideEarth()
    {
        var session = new LabSession();
        var initial = session.Snapshot();
        session.Execute(new(initial.InstanceId, initial.Revision, Client, "configure",
            DurationSeconds: 2592000, Mode: "continuous"));
        var preview = Assert.IsType<CoursePreview>(session.PreviewAutomaticCourse(SolarSystem.Earth, null, CancellationToken.None).Fastest);
        var schedule = preview.Trajectory.BurnSchedule!;
        Send(session, "commit-course", preview.PreviewId);
        var departureCutoff = long.Parse(schedule[0].StartTicks) + (long)Math.Round(schedule[0].DurationSeconds * TimeSpan.TicksPerSecond);
        foreach (var (kind, ticks) in new[]
        {
            ("course-departure-cutoff", departureCutoff),
            ("course-braking-start", long.Parse(schedule[1].StartTicks)),
            ("course-approach-complete", long.Parse(preview.Trajectory.ArrivalTicks))
        })
        {
            Assert.True(Send(session, "play").Running);
            Assert.Equal(kind == "course-approach-complete" ? "Pathfinder arrived at Earth." : null,
                session.TickWithArrival());
            var interrupted = session.Snapshot();
            Assert.False(interrupted.Running);
            Assert.Equal(kind, interrupted.Interrupt!.Kind);
            Assert.Equal(ticks, interrupted.Interrupt.AtTicks);
            Assert.Equal(ticks, interrupted.World.ElapsedTicks);
            Assert.Null(session.TickWithArrival());
            Assert.Equal(interrupted.Revision, session.Snapshot().Revision);
        }

        var after = session.Snapshot();
        var spacecraft = after.World.Objects.Single(body => body.Id == SystemLab.ShipId);
        var earth = after.World.Objects.Single(body => body.Id == SolarSystem.Earth);
        Assert.Equal(preview.Trajectory.Points[^1].PositionKm, spacecraft.Position);
        Assert.InRange(spacecraft.Position.DistanceTo(earth.Position), 1_000_000, 1_030_000);
        Assert.Equal("Coasting", after.World.Ship.Status);
        Assert.Equal("approach", after.World.Ship.ArrivalKind);
        Assert.Equal("Velocity-matched approach complete", after.StopReason);
        Assert.Equal(after.StopReason, after.Interrupt!.ArrivalLabel);
        Assert.Contains("approach point", after.Interrupt!.Message);
        Assert.Equal(preview.Trajectory.BrakingMarginKg, after.World.Ship.Propulsion!.AvailablePropellantKg);
    }

    [Fact]
    public void NoRouteAndCancellationLeaveTheSessionUntouched()
    {
        var session = new LabSession();
        var before = session.Snapshot();
        var result = session.PreviewAutomaticCourse("planet-jupiter", 1, CancellationToken.None);
        Assert.Null(result.Fastest);
        Assert.Null(result.LowestManeuverCost);
        Assert.Contains(result.InfeasibleReasons, reason => reason.Contains("Insufficient propellant"));
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

    [Fact]
    public void SearchKeepsEvaluationCountBoundedWhenRefiningFeasibleDurations()
    {
        var sampled = new HashSet<int>();
        var plan = SystemLab.CreateLegacyFixture().PreviewTravel("planet-iris");
        var options = CourseSearch.Find(seconds =>
        {
            Assert.True(sampled.Add(seconds));
            return seconds >= 200_000 ? plan : null;
        }, CancellationToken.None);

        Assert.NotNull(options);
        Assert.InRange(sampled.Count, 41, 56);
    }

    private static SessionSnapshot Send(LabSession session, string operation, string? previewId = null)
    {
        var state = session.Snapshot();
        return session.Execute(new(state.InstanceId, state.Revision, Client, operation,
            DurationSeconds: 2592000, Mode: "once", PreviewId: previewId));
    }
}
