using System.Diagnostics;
using Quasar.Server;
using Quasar.Simulation;
using Xunit.Abstractions;

namespace Quasar.Simulation.Tests;

public sealed class TrajectoryTests(ITestOutputHelper output)
{
    private const double Mu = 398600.435;
    private static readonly CelestialState Initial = new(new(7000, 0, 0), new(0, Math.Sqrt(Mu / 7000), 0));
    private static CelestialPositionService Catalogue() => new([
        new("primary", "Primary", CelestialBodyKind.Planet, Mu),
        new("target", "Target", CelestialBodyKind.Moon, 0, "primary", new(12000, .1, .2, .3, .4, 1))
    ]);
    private static long Ticks(double seconds) => checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    private static void Near(Position3 expected, Position3 actual, double tolerance = 1e-5) =>
        Assert.InRange(expected.DistanceTo(actual), 0, tolerance);
    private static double Speed(Velocity3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    private static MovementExecution World(CelestialPositionService catalogue, string id = "ship")
    {
        var world = new MovementExecution(catalogue); world.AddSpacecraft(id, Initial, "primary"); return world;
    }

    [Fact]
    public void CircularPropagationMatchesIndependentQuarterOrbitAndLargeEpoch()
    {
        var coast = new TwoBodyTrajectory(Initial, 0, Mu);
        var period = 2 * Math.PI * Math.Sqrt(Math.Pow(7000, 3) / Mu);
        Near(new(0, 7000, 0), coast.StateAt(Ticks(period / 4)).PositionKm, 1e-6);
        Near(new(0, -7000, 0), coast.StateAt(Ticks(-period / 4)).PositionKm, 1e-6);
        var late = coast.StateAt(long.MaxValue);
        Assert.InRange(late.PositionKm.DistanceTo(default), 6999.99999, 7000.00001);
        Assert.Equal(late, coast.StateAt(long.MaxValue));
    }

    [Theory]
    [InlineData(7.5)]
    [InlineData(10.671731)]
    [InlineData(12)]
    public void EllipticNearParabolicAndHyperbolicPropagationConserveEnergyAndMomentum(double speed)
    {
        var state = new CelestialState(new(7000, 0, 0), new(0, speed, 0));
        var coast = new TwoBodyTrajectory(state, 0, Mu);
        var later = coast.StateAt(Ticks(20000));
        var energy = speed * speed / 2 - Mu / 7000;
        Assert.InRange(Math.Abs(Speed(later.VelocityKmPerSecond) * Speed(later.VelocityKmPerSecond) / 2
            - Mu / later.PositionKm.DistanceTo(default) - energy), 0, 1e-8);
        var h = later.PositionKm.X * later.VelocityKmPerSecond.Y - later.PositionKm.Y * later.VelocityKmPerSecond.X;
        Assert.InRange(Math.Abs(h - 7000 * speed), 0, 1e-5);
        var backwards = new TwoBodyTrajectory(later, Ticks(20000), Mu).StateAt(0);
        Near(state.PositionKm, backwards.PositionKm, 1e-4);
    }

    [Fact]
    public void LambertRecoversKnownCircularTransferAndCurvesAwayFromChord()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat);
        var seconds = Math.PI / 2 * Math.Sqrt(Math.Pow(7000, 3) / Mu);
        var plan = planner.Plan(new(Initial, 0,
            new("primary", Ticks(seconds), new(null, new(new(0, 7000, 0), new(-Math.Sqrt(Mu / 7000), 0, 0)), Ticks(seconds)))));
        Assert.InRange(plan.RequiredDeltaVKmPerSecond, 0, 1e-8);
        var midpoint = plan.StateAt(Ticks(seconds / 2));
        Near(new(7000 / Math.Sqrt(2), 7000 / Math.Sqrt(2), 0), midpoint.PositionKm, 1e-5);
        Assert.True(midpoint.PositionKm.DistanceTo(new(3500, 3500, 0)) > 2000);
    }

    [Theory]
    [InlineData(false, 2500)]
    [InlineData(true, 7500)]
    [InlineData(false, 150)]
    public void FutureCelestialPositionIsInterceptedForBothArcsAndFastHyperbolicTransfer(bool longWay, int seconds)
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat);
        var plan = planner.Plan(new(Initial, 0, new("primary", Ticks(seconds), new("target"), longWay)));
        var endpoint = cat.GetState("target", plan.ArrivalTicks);
        Near(endpoint.PositionKm, plan.Segments[0].StateAt(plan.ArrivalTicks).PositionKm);
        Assert.Equal(endpoint, plan.StateAt(plan.ArrivalTicks));
        Assert.True(endpoint.PositionKm.DistanceTo(cat.GetState("target", 0).PositionKm) > 100);
        Assert.True(plan.RequiredDeltaVKmPerSecond > 0);
        Assert.Null(plan.Fuel.PropellantMassKg);
        Assert.Contains("propulsion", plan.Fuel.UnavailableReason);
    }

    [Fact]
    public void MovingInertialDestinationAndMovingPrimaryUseConsistentFrames()
    {
        var cat = new CelestialPositionService([
            new("primary", "Drifting primary", CelestialBodyKind.Star, Mu,
                ReferenceState: new(new(1e6, 2e6, 3e6), new(5, -2, 1)))
        ]);
        var initial = new CelestialState(new(1e6 + 7000, 2e6, 3e6), new(5, Initial.VelocityKmPerSecond.Y - 2, 1));
        var target = new TrajectoryDestination(null, new(new(1e6, 2e6 + 12000, 3e6), new(6, -1, 2)));
        var plan = new TrajectoryPlanner(cat).Plan(new(initial, 0, new("primary", Ticks(2000), target)));
        Near(target.StateAt(cat, Ticks(2000)).PositionKm, plan.Segments[0].StateAt(Ticks(2000)).PositionKm);
        Assert.Equal(target.StateAt(cat, Ticks(2000)), plan.StateAt(Ticks(2000)));
    }

    [Fact]
    public void PreviewAndSamplingNeverChangeExecutionAndCommittedQueriesAreDeterministic()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat); var world = World(cat);
        var before = world.View("ship");
        var request = new TransferRequest(before.State, 0, new("primary", Ticks(2500), new("target")));
        var plan = planner.Plan(request);
        Assert.Same(plan, planner.Plan(request));
        var points = plan.Sample(); points[0] = points[^1];
        Assert.Equal(before, world.View("ship")); Assert.Null(world.CommittedPlan("ship"));
        world.Commit("ship", plan);
        var at = Ticks(1200); var expected = plan.StateAt(at);
        Parallel.For(0, 100, _ => Assert.Equal(expected, world.StateAt("ship", at)));
        Assert.Equal(0, world.ElapsedTicks);
        foreach (var sample in plan.Sample()) Assert.Equal(sample.State, world.StateAt("ship", sample.AtTicks));
    }

    [Fact]
    public void MaplessLargeAndSmallAdvancesAgreeAndArrivalIsNotSkipped()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat);
        var large = World(cat); var small = World(cat);
        var plan = planner.Plan(new(Initial, 0, new("primary", Ticks(2500), new("target"))));
        large.Commit("ship", plan); small.Commit("ship", plan);
        Assert.Empty(large.Advance(TimeSpan.FromSeconds(2000)));
        for (var i = 0; i < 2000; i++) Assert.Empty(small.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(large.View("ship"), small.View("ship"));
        var arrival = Assert.Single(large.Advance(TimeSpan.FromDays(30)));
        Assert.Equal(Ticks(2500), arrival.AtTicks); Assert.Equal("arrival", arrival.Kind);
        while (small.Advance(TimeSpan.FromSeconds(1)).Length == 0) { }
        Assert.Equal(large.View("ship"), small.View("ship"));
        Assert.Empty(large.Advance(TimeSpan.FromDays(30)));
        Assert.Equal(cat.GetState("target", large.ElapsedTicks), large.StateAt("ship", large.ElapsedTicks));
    }

    [Fact]
    public void CancellationPreservesMomentumAndReplanningRemovesObsoleteArrival()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat); var world = World(cat);
        var first = planner.Plan(new(Initial, 0, new("primary", Ticks(2500), new("target"))));
        world.Commit("ship", first); world.Advance(TimeSpan.FromSeconds(1000));
        var before = world.View("ship").State;
        world.Cancel("ship"); Assert.Equal(before, world.View("ship").State);
        Near(first.StateAt(Ticks(2000)).PositionKm, world.StateAt("ship", Ticks(2000)).PositionKm);
        Assert.Throws<InvalidOperationException>(() => world.Commit("ship", first));
        var second = planner.Plan(new(world.View("ship").State, world.ElapsedTicks,
            new("primary", Ticks(4000), new(null, new(new(-11000, 3000, 500), default)))));
        world.Commit("ship", second);
        Assert.Empty(world.Advance(TimeSpan.FromSeconds(1600))); // Past discarded first arrival.
        var arrival = Assert.Single(world.Advance(TimeSpan.FromDays(30)));
        Assert.Equal(Ticks(4000), arrival.AtTicks);
        Assert.Throws<InvalidOperationException>(() => world.Cancel("ship"));
    }

    [Fact]
    public void InvalidPlansAndStaleCataloguesAreRejectedBeforeOrdersChange()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat); var world = World(cat);
        var leg = new TransferLeg("primary", Ticks(2500), new("target"));
        var plan = planner.Plan(new(Initial, 0, leg)); world.Commit("ship", plan);
        Assert.Throws<ArgumentException>(() => planner.Plan(new(Initial, 0, leg, 0)));
        Assert.Throws<ArgumentException>(() => planner.Plan(new(Initial, 0, leg with { ArrivalTicks = 0 })));
        Assert.Throws<ArgumentException>(() => planner.Plan(new(Initial, 0, leg with { MinimumPrimaryClearanceKm = 1e6 })));
        Assert.Throws<ArgumentException>(() => planner.Plan(new(Initial, 0, leg with { PrimaryBodyId = "missing" })));
        Assert.Throws<ArgumentException>(() => planner.Plan(new(Initial, 0, leg with { Destination = new(null, new(Initial.PositionKm, default)) })));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Sample(100000));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Advance(TimeSpan.FromTicks(-1)));
        var revised = cat.WithBody(cat.Bodies["target"] with { Orbit = new(13000, .1) });
        var revisedWorld = World(revised);
        Assert.Throws<InvalidOperationException>(() => revisedWorld.Commit("ship", plan));
        Assert.Same(plan, world.CommittedPlan("ship"));
    }

    [Fact]
    public void PatchedConicsAreContinuousAtSoiAndEveryBoundaryInterruptsLargeJumps()
    {
        var cat = new CelestialPositionService([
            new("star", "Star", CelestialBodyKind.Star, 1e6),
            new("planet", "Planet", CelestialBodyKind.Planet, 1000, "star", new(100000, 0))
        ]);
        var soi = cat.GetEnvironment("planet").SphereOfInfluenceRadiusKm!.Value;
        var planetAtPatch = cat.GetState("planet", Ticks(10000)).PositionKm;
        var waypoint = new Position3(planetAtPatch.X + soi, planetAtPatch.Y, planetAtPatch.Z);
        var planetAtEnd = cat.GetState("planet", Ticks(15000)).PositionKm;
        var target = new Position3(planetAtEnd.X, planetAtEnd.Y + 2000, planetAtEnd.Z + 100);
        var initial = new CelestialState(new(70000, 0, 0), new(0, Math.Sqrt(1e6 / 70000), 0));
        TransferLeg[] legs = [new("star", Ticks(10000), new(null, new(waypoint, default))),
            new("planet", Ticks(15000), new(null, new(target, default)))];
        var planner = new TrajectoryPlanner(cat);
        var plan = planner.PlanPatched(initial, 0, legs);
        Near(plan.Segments[0].StateAt(Ticks(10000)).PositionKm, plan.Segments[1].StateAt(Ticks(10000)).PositionKm);
        Assert.Equal("patch", plan.Burns[1].Phase);
        Assert.Contains(plan.Sample(), p => p.AtTicks == Ticks(10000));
        var world = new MovementExecution(cat); world.AddSpacecraft("ship", initial, "star"); world.Commit("ship", plan);
        Assert.Equal("patch", Assert.Single(world.Advance(TimeSpan.FromDays(30))).Kind);
        Assert.Equal(Ticks(10000), world.ElapsedTicks);
        Assert.Equal(plan.StateAt(world.ElapsedTicks), world.View("ship").State);
        Assert.Equal("arrival", Assert.Single(world.Advance(TimeSpan.FromDays(30))).Kind);
        var invalid = legs.ToArray(); invalid[0] = invalid[0] with { Destination = new(null, new(new(90000, 20000, 0), default)) };
        Assert.Throws<ArgumentException>(() => planner.PlanPatched(initial, 0, invalid));
    }

    [Fact]
    public void LabPresentationAndSessionPreviewAreReadOnlyAndMatchAuthoritativeState()
    {
        var session = new LabSession(SystemLab.CreateLegacyFixture()); var before = session.Snapshot();
        var preview = session.PreviewTravel("planet-iris", 21600);
        var after = session.Snapshot();
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(before.World.Objects, after.World.Objects);
        Assert.Equal(before.World.Events, after.World.Events); Assert.Null(after.World.Trajectory);
        var committed = session.Execute(new(before.InstanceId, before.Revision, Guid.NewGuid().ToString(), "travel", DestinationId: "planet-iris"));
        Assert.Equal(preview.Points, committed.World.Trajectory!.Points);
        foreach (var point in preview.Points)
            Assert.Equal(point.PositionKm, session.ShipStateAt(long.Parse(point.AtTicks)).PositionKm);
        committed.World.Trajectory.Points[0] = committed.World.Trajectory.Points[^1];
        Assert.Equal(preview.Points, session.Snapshot().World.Trajectory!.Points);
    }

    [Fact]
    public void ThousandShipMeasurementSeparatesSolvingQueriesAndEventAdvancement()
    {
        var cat = Catalogue(); var planner = new TrajectoryPlanner(cat); var world = new MovementExecution(cat);
        var timer = Stopwatch.StartNew();
        var plan = planner.Plan(new(Initial, 0, new("primary", Ticks(2500), new("target"))));
        timer.Stop(); var solve = timer.Elapsed.TotalMilliseconds;
        for (var i = 0; i < 1000; i++) { var id = $"ship-{i:D4}"; world.AddSpacecraft(id, Initial, "primary"); world.Commit(id, plan); }
        timer.Restart();
        for (var i = 0; i < 1000; i++) world.StateAt($"ship-{i:D4}", Ticks(1200));
        timer.Stop(); var queries = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); Assert.Empty(world.Advance(TimeSpan.FromSeconds(2000))); timer.Stop(); var advance = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); var events = world.Advance(TimeSpan.FromDays(30)); timer.Stop();
        Assert.Equal(1000, events.Length); Assert.Equal(Ticks(2500), world.ElapsedTicks);
        output.WriteLine($"Release sample, one shared plan: solve {solve:F3} ms; 1000 queries {queries:F3} ms; no-event advance {advance:F3} ms; 1000 arrivals {timer.Elapsed.TotalMilliseconds:F3} ms. Not a capacity guarantee.");
    }
}
