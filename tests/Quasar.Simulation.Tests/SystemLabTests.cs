using System.Diagnostics;
using Quasar.Server;
using Quasar.Simulation;
using Xunit.Abstractions;

namespace Quasar.Simulation.Tests;

public sealed class SystemLabTests(ITestOutputHelper output)
{
    [Fact]
    public void FixtureHasStableIdentitiesAndThreeDimensionalPositions()
    {
        var first = new SystemLab().Snapshot();
        var second = new SystemLab().Snapshot();
        Assert.Equal(4, first.Objects.Length);
        Assert.Equal(4, first.Objects.Select(o => o.Id).Distinct().Count());
        Assert.Equal(first.Objects, second.Objects);
        Assert.Single(first.Objects, o => o.Kind == "star");
        Assert.Equal(2, first.Objects.Count(o => o.Kind == "planet"));
        Assert.NotEqual(0, Ship(first).Position.Z);
        Assert.Equal(0, first.ElapsedTicks);
    }

    [Fact]
    public void ClockHandlesZeroNegativeAndOverflowAtomically()
    {
        var lab = new SystemLab();
        Assert.False(lab.Advance(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => lab.Advance(TimeSpan.FromTicks(-1)));
        lab.Advance(TimeSpan.MaxValue);
        Assert.Throws<OverflowException>(() => lab.Advance(TimeSpan.FromTicks(1)));
        Assert.Equal(long.MaxValue, lab.ElapsedTicks);
        Assert.Throws<OverflowException>(() => lab.OrderTravel("planet-iris"));
        Assert.Equal("Idle", lab.Snapshot().Ship.Status);
        Assert.Single(lab.Snapshot().Events);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    [InlineData(432000)]
    [InlineData(1296000)]
    [InlineData(2592000)]
    public void IdleClockAdvancesExactly(int seconds)
    {
        var lab = new SystemLab();
        lab.Advance(TimeSpan.FromSeconds(seconds));
        Assert.Equal(seconds * TimeSpan.TicksPerSecond, lab.ElapsedTicks);
    }

    [Fact]
    public void MovementUsesAllAxesAndEquivalentIncrementsAgree()
    {
        var large = new SystemLab();
        var small = new SystemLab();
        large.OrderTravel("planet-iris"); small.OrderTravel("planet-iris");
        large.Advance(TimeSpan.FromHours(1));
        for (var i = 0; i < 3600; i++) small.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(large.Snapshot().Objects, small.Snapshot().Objects);
        Assert.Equal(large.ElapsedTicks, small.ElapsedTicks);
        var p = Ship(large.Snapshot()).Position;
        Assert.True(p.X > 900_000 && p.Y > -200_000 && p.Z < 350_000);
        var expectedDistance = 25 * 3600;
        Assert.InRange(new Position3(900_000, -200_000, 350_000).DistanceTo(p), expectedDistance - .000001, expectedDistance + .000001);
    }

    [Theory]
    [InlineData(86400)]
    [InlineData(432000)]
    [InlineData(1296000)]
    [InlineData(2592000)]
    public void LongAdvanceAndMinuteAdvancesStopAtSameExactArrival(int seconds)
    {
        var large = new SystemLab(); var small = new SystemLab();
        large.OrderTravel("planet-nereid"); small.OrderTravel("planet-nereid");
        var arrival = large.Snapshot().Ship.ArrivalTicks;
        while (!large.Advance(TimeSpan.FromSeconds(seconds))) { }
        while (!small.Advance(TimeSpan.FromMinutes(1))) { }
        Assert.Equal(arrival, large.ElapsedTicks);
        Assert.Equal(large.ElapsedTicks, small.ElapsedTicks);
        Assert.Equal(large.Snapshot().Objects, small.Snapshot().Objects);
        Assert.Equal(large.Snapshot().Events, small.Snapshot().Events);
        Assert.Equal(large.Snapshot().Objects.Single(o => o.Id == "planet-nereid").Position, Ship(large.Snapshot()).Position);
        large.Advance(TimeSpan.FromDays(30));
        Assert.Single(large.Snapshot().Events, e => e.Kind == "arrival");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    public void ShortIntervalStopsAtArrivalInsideItsDeadline(int seconds)
    {
        var lab = new SystemLab(); lab.OrderTravel("planet-iris");
        var arrival = lab.Snapshot().Ship.ArrivalTicks!.Value;
        Assert.False(lab.Advance(TimeSpan.FromTicks(arrival - seconds * TimeSpan.TicksPerSecond + 1)));
        Assert.True(lab.Advance(TimeSpan.FromSeconds(seconds)));
        Assert.Equal(arrival, lab.ElapsedTicks);
        Assert.Equal(arrival, Assert.Single(lab.Snapshot().Events, e => e.Kind == "arrival").AtTicks);
    }

    [Fact]
    public void SwitchingShortIntervalsPreservesExactMovementAndHistory()
    {
        var mixed = new SystemLab(); var single = new SystemLab();
        mixed.OrderTravel("planet-iris"); single.OrderTravel("planet-iris");
        int[] seconds = [1, 5, 30, 60, 3600, 30, 5, 1];
        foreach (var duration in seconds) Assert.False(mixed.Advance(TimeSpan.FromSeconds(duration)));
        Assert.False(single.Advance(TimeSpan.FromSeconds(seconds.Sum())));
        Assert.Equal(single.ElapsedTicks, mixed.ElapsedTicks);
        Assert.Equal(single.Snapshot().Objects, mixed.Snapshot().Objects);
        Assert.Equal(single.Snapshot().Ship, mixed.Snapshot().Ship);
        Assert.Equal(single.Snapshot().Events, mixed.Snapshot().Events);
    }

    [Fact]
    public void RepeatedIntervalChangesHaveNoClockDrift()
    {
        var session = new LabSession(new TestClock());
        int[] seconds = [1, 5, 30, 60, 3600, 86400, 432000, 1296000, 2592000];
        long expectedTicks = 0;
        for (var cycle = 0; cycle < 100; cycle++)
        {
            // Exercise increasing and decreasing intervals, including all legacy settings.
            foreach (var duration in seconds.Concat(seconds.Reverse()))
            {
                Send(session, "configure", duration, "once");
                Assert.Equal(expectedTicks, session.Snapshot().World.ElapsedTicks);
                Send(session, "play"); session.Tick();
                expectedTicks += duration * TimeSpan.TicksPerSecond;
                var state = session.Snapshot();
                Assert.Equal(expectedTicks, state.World.ElapsedTicks);
                Assert.False(state.Running);
                Assert.Equal(duration, state.DurationSeconds);
            }
        }
        Assert.Single(session.Snapshot().World.Events);
        Assert.Equal(new SystemLab().Snapshot().Objects, session.Snapshot().World.Objects);
    }

    [Fact]
    public void ArrivalBoundaryAndRetargetingAreExact()
    {
        var lab = new SystemLab(); lab.OrderTravel("planet-iris");
        lab.Advance(TimeSpan.FromHours(1));
        var departure = Ship(lab.Snapshot()).Position;
        lab.OrderTravel("planet-nereid");
        Assert.Equal(departure, Ship(lab.Snapshot()).Position);
        var arrival = lab.Snapshot().Ship.ArrivalTicks!.Value;
        Assert.False(lab.Advance(TimeSpan.FromTicks(arrival - lab.ElapsedTicks - 1)));
        Assert.True(lab.Advance(TimeSpan.FromTicks(1)));
        Assert.Equal(arrival, lab.ElapsedTicks);
        Assert.Contains("Nereid", lab.Snapshot().Events.Single(e => e.Kind == "arrival").Message);
    }

    [Fact]
    public void InvalidAndDuplicateOrdersPreserveState()
    {
        var lab = new SystemLab();
        Assert.Throws<ArgumentException>(() => lab.OrderTravel("star-helios"));
        Assert.Throws<ArgumentException>(() => lab.OrderTravel("missing"));
        lab.OrderTravel("planet-iris");
        Assert.Throws<InvalidOperationException>(() => lab.OrderTravel("planet-iris"));
        lab.Advance(TimeSpan.FromDays(30));
        Assert.Throws<InvalidOperationException>(() => lab.OrderTravel("planet-iris"));
        Assert.Equal(3, lab.Snapshot().Events.Length);
    }

    [Fact]
    public void SnapshotsCannotMutateTheWorld()
    {
        var lab = new SystemLab(); var snapshot = lab.Snapshot();
        snapshot.Objects[0] = snapshot.Objects[0] with { Name = "Tampered" };
        snapshot.Events[0] = snapshot.Events[0] with { Message = "Tampered" };
        Assert.Equal("Helios", lab.Snapshot().Objects[0].Name);
        Assert.DoesNotContain("Tampered", lab.Snapshot().Events[0].Message);
    }

    [Fact]
    public void LargeAdvancesHaveNoPerSecondLoop()
    {
        var lab = new SystemLab(); lab.Advance(TimeSpan.Zero);
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++) lab.Advance(TimeSpan.FromDays(30));
        timer.Stop();
        output.WriteLine($"100,000 idle 30-day advances: {timer.Elapsed.TotalMilliseconds:F3} ms (not a scale guarantee).");
        Assert.Equal(100_000L * TimeSpan.TicksPerDay * 30, lab.ElapsedTicks);
    }

    [Fact]
    public void OnceStopsAndContinuousProcessesChosenIntervals()
    {
        var session = new LabSession(new TestClock());
        Send(session, "play"); session.Tick();
        Assert.False(session.Snapshot().Running);
        Assert.Equal(TimeSpan.TicksPerHour, session.Snapshot().World.ElapsedTicks);
        Send(session, "configure", 60, "continuous"); Send(session, "play");
        session.Tick(); session.Tick();
        Assert.True(session.Snapshot().Running);
        Assert.Equal(TimeSpan.TicksPerHour + 120 * TimeSpan.TicksPerSecond, session.Snapshot().World.ElapsedTicks);
        Send(session, "stop"); var ticks = session.Snapshot().World.ElapsedTicks; session.Tick();
        Assert.Equal(ticks, session.Snapshot().World.ElapsedTicks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    [InlineData(432000)]
    [InlineData(1296000)]
    [InlineData(2592000)]
    public void EveryIntervalSupportsOnceContinuousStopAndResume(int seconds)
    {
        var clock = new TestClock(); var session = new LabSession(clock);
        Send(session, "configure", seconds, "once"); Send(session, "play"); session.Tick();
        Assert.False(session.Snapshot().Running);
        Assert.Equal(seconds * TimeSpan.TicksPerSecond, session.Snapshot().World.ElapsedTicks);

        Send(session, "configure", seconds, "continuous"); Send(session, "play");
        session.Tick(); session.Tick();
        var running = session.Snapshot();
        Assert.True(running.Running);
        Assert.Equal(3L * seconds * TimeSpan.TicksPerSecond, running.World.ElapsedTicks);
        Assert.Throws<InvalidOperationException>(() => Send(session, "configure", 60, "once"));
        Assert.Equal(running.Revision, session.Snapshot().Revision);

        Send(session, "stop");
        clock.Now = clock.Now.AddDays(1); session.Tick();
        Assert.Equal(running.World.ElapsedTicks, session.Snapshot().World.ElapsedTicks);
        Assert.Equal(seconds, session.Snapshot().DurationSeconds);
        Assert.Equal("continuous", session.Snapshot().Mode);

        Send(session, "play"); session.Tick();
        Assert.True(session.Snapshot().Running);
        Assert.Equal(4L * seconds * TimeSpan.TicksPerSecond, session.Snapshot().World.ElapsedTicks);
    }

    [Theory]
    [InlineData(432000, "once")]
    [InlineData(432000, "continuous")]
    [InlineData(1296000, "once")]
    [InlineData(1296000, "continuous")]
    [InlineData(2592000, "continuous")]
    public void ArrivalInterruptDiscardsRemainderAndResumeUsesFullInterval(int seconds, string mode)
    {
        var session = new LabSession(new TestClock());
        Send(session, "travel", destination: "planet-iris");
        var arrival = session.Snapshot().World.Ship.ArrivalTicks;
        Send(session, "configure", seconds, mode); Send(session, "play"); session.Tick();
        Assert.False(session.Snapshot().Running);
        Assert.Equal(arrival, session.Snapshot().World.ElapsedTicks);
        Assert.Contains("Arrival", session.Snapshot().StopReason);
        session.Tick();
        Assert.Equal(arrival, session.Snapshot().World.ElapsedTicks);
        Send(session, "play"); session.Tick();
        Assert.Equal(arrival + seconds * TimeSpan.TicksPerSecond, session.Snapshot().World.ElapsedTicks);
        Assert.Single(session.Snapshot().World.Events, e => e.Kind == "arrival");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(432000)]
    [InlineData(1296000)]
    public void LeaseExpiresAndOnlyItsOwnerCanRenewIt(int seconds)
    {
        var clock = new TestClock(); var session = new LabSession(clock);
        Send(session, "configure", seconds, "continuous");
        var play = Send(session, "play");
        clock.Now = clock.Now.AddSeconds(4);
        session.RenewLease(play.InstanceId, Guid.NewGuid().ToString());
        clock.Now = clock.Now.AddSeconds(1); session.Tick();
        Assert.False(session.Snapshot().Running);
        Assert.Equal(0, session.Snapshot().World.ElapsedTicks);
        Send(session, "play"); clock.Now = clock.Now.AddSeconds(4);
        session.RenewLease(play.InstanceId, Client); clock.Now = clock.Now.AddSeconds(2); session.Tick();
        Assert.True(session.Snapshot().Running);
    }

    [Fact]
    public void StaleCommandsAndOtherInstancesAreRejectedButStopCannotLoseToATick()
    {
        var session = new LabSession(new TestClock()); var old = session.Snapshot();
        Send(session, "configure", 1, "continuous"); Send(session, "play"); session.Tick();
        Assert.Throws<InvalidOperationException>(() => session.Execute(new(old.InstanceId, old.Revision, Client, "travel", DestinationId: "planet-iris")));
        Assert.Throws<InvalidOperationException>(() => session.Execute(new("another-instance", old.Revision, Client, "stop")));
        session.Execute(new(old.InstanceId, old.Revision, Client, "stop"));
        Assert.False(session.Snapshot().Running);
    }

    [Theory]
    [InlineData("configure", -1, "once")]
    [InlineData("configure", 0, "once")]
    [InlineData("configure", 6, "once")]
    [InlineData("configure", 3600, "bad")]
    [InlineData("unknown", 3600, "once")]
    public void InvalidSessionCommandsAreAtomic(string operation, int duration, string mode)
    {
        var session = new LabSession(new TestClock()); var before = session.Snapshot();
        Assert.Throws<ArgumentException>(() => Send(session, operation, duration, mode));
        Assert.Equal(before.Revision, session.Snapshot().Revision);
        Assert.Equal(before.DurationSeconds, session.Snapshot().DurationSeconds);
    }

    [Fact]
    public void ConcurrentCommandsHaveOneWriterAndRejectObsoleteRevision()
    {
        var session = new LabSession(new TestClock()); var state = session.Snapshot(); var accepted = 0;
        Parallel.For(0, 20, _ =>
        {
            try { session.Execute(new(state.InstanceId, state.Revision, Client, "travel", DestinationId: "planet-iris")); Interlocked.Increment(ref accepted); }
            catch (InvalidOperationException) { }
        });
        Assert.Equal(1, accepted);
        Assert.Equal(2, session.Snapshot().World.Events.Length);
    }

    private const string Client = "2d46bd17-2aca-4cb6-9c03-4c3587a52f59";
    private static LabObject Ship(LabSnapshot snapshot) => snapshot.Objects.Single(o => o.Id == SystemLab.ShipId);
    private static SessionSnapshot Send(LabSession session, string operation, int? duration = null, string? mode = null, string? destination = null)
    {
        var state = session.Snapshot();
        return session.Execute(new(state.InstanceId, state.Revision, Client, operation, duration, mode, destination));
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
