using System.Text;
using System.Text.Json.Nodes;
using Quasar.Simulation;

namespace Quasar.Simulation.Tests;

public sealed class SimulationStateTests
{
    private const string Helios = "star-helios";
    private static readonly CelestialState PatchedInitial = new(new(70000, 0, 0), new(0, Math.Sqrt(1e6 / 70000), 0));

    private static long Ticks(int seconds) => (long)seconds * TimeSpan.TicksPerSecond;

    [Fact]
    public void LabMidflightJsonRoundTripRetainsSolvedPlanAndExactFifteenDayInterruption()
    {
        var original = new SystemLab();
        original.OrderTravel("planet-iris");
        original.Advance(TimeSpan.FromHours(2));

        var saved = original.ExportState();
        var bytes = SimulationStateJson.Serialize(saved);
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(bytes));
        Assert.Equal(bytes, SimulationStateJson.Serialize(original.ExportState()));
        Assert.Equal(bytes, SimulationStateJson.Serialize(restored.ExportState()));
        var arrival = restored.Snapshot().Ship.ArrivalTicks!.Value;
        Assert.Equal(original.Snapshot().Ship, restored.Snapshot().Ship);
        Assert.Equal(original.Snapshot().Trajectory!.Points, restored.Snapshot().Trajectory!.Points);
        foreach (var at in new[] { original.ElapsedTicks, Ticks(10000), arrival - 1, arrival, arrival + Ticks(3600) })
            Assert.Equal(original.ShipStateAt(at), restored.ShipStateAt(at));

        var jump = TimeSpan.FromDays(15);
        Assert.True(original.Advance(jump));
        Assert.True(restored.Advance(jump));
        Assert.Equal(arrival, restored.ElapsedTicks);
        Assert.Equal(original.Snapshot().Events, restored.Snapshot().Events);
        var arrivalSave = SimulationStateJson.Serialize(restored.ExportState());
        restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(arrivalSave));
        Assert.Equal("Arrived", restored.Snapshot().Ship.Status);
        Assert.False(original.Advance(jump));
        Assert.False(restored.Advance(jump));
        Assert.Equal(arrival + jump.Ticks, restored.ElapsedTicks);
        Assert.Equal(original.ShipStateAt(restored.ElapsedTicks), restored.ShipStateAt(restored.ElapsedTicks));
        Assert.Single(restored.Snapshot().Events, entry => entry.Kind == "arrival");

        original.OrderTravel("planet-nereid");
        restored.OrderTravel("planet-nereid");
        Assert.Equal(original.Snapshot().Events, restored.Snapshot().Events);
        Assert.Equal(4L, restored.Snapshot().Events[^1].Sequence);
        Assert.Equal(bytes, SimulationStateJson.Serialize(saved));
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(10000)]
    public void PatchedPlanRestoresOnEitherSideOfConsumedBoundaryWithStableSimultaneousEvents(int checkpointSeconds)
    {
        var original = PatchedWorld();
        original.Advance(TimeSpan.FromSeconds(checkpointSeconds));
        var saved = original.ExportState();
        var restored = MovementExecution.RestoreState(saved);
        var ship = saved.Spacecraft[0];
        var trajectory = ship.Trajectory!;
        Assert.Equal(checkpointSeconds == 5000 ? Helios : "planet", ship.PrimaryBodyId);
        Assert.Equal(checkpointSeconds == 5000 ? 0 : 1, trajectory.NextBoundaryIndex);
        Assert.Equal(original.CommittedPlan("ship-a")!.Burns, restored.CommittedPlan("ship-a")!.Burns);
        Assert.Equal(original.View("ship-a"), restored.View("ship-a"));
        foreach (var at in new[] { Ticks(checkpointSeconds), Ticks(10000), Ticks(12000), Ticks(15000), Ticks(18000) })
            Assert.Equal(original.StateAt("ship-a", at), restored.StateAt("ship-a", at));

        var jump = TimeSpan.FromDays(15);
        if (checkpointSeconds < 10000)
            CompareBoundary(original, restored, jump, Ticks(10000), "patch");
        CompareBoundary(original, restored, jump, Ticks(15000), "arrival");
        Assert.Empty(original.Advance(jump));
        Assert.Empty(restored.Advance(jump));
        Assert.Equal(original.View("ship-a"), restored.View("ship-a"));
        Assert.Equal("Arrived", restored.View("ship-a").Status);
        Assert.Equal(original.StateAt("ship-a", Ticks(15000) + jump.Ticks + Ticks(4321)),
            restored.StateAt("ship-a", Ticks(15000) + jump.Ticks + Ticks(4321)));
    }

    [Fact]
    public void CancellationAtPatchKeepsTheNewPrimaryAndRestoresTheActualCoast()
    {
        var original = PatchedWorld();
        original.Advance(TimeSpan.FromDays(15));
        var atCancel = original.View("ship-a");
        original.Cancel("ship-a");
        original.Cancel("ship-z");
        var saved = original.ExportState();
        var ship = saved.Spacecraft[0];
        Assert.Equal("planet", ship.PrimaryBodyId);
        Assert.Equal(atCancel.AtTicks, ship.FreeCoast!.EpochTicks);
        Assert.Null(ship.Trajectory);

        var restored = MovementExecution.RestoreState(saved);
        var cancelled = restored.View("ship-a");
        Assert.Equal("Coasting", cancelled.Status);
        Assert.Equal(atCancel.State, cancelled.State);
        foreach (var at in new[] { Ticks(10000), Ticks(14000), Ticks(15000), Ticks(17500) })
            Assert.Equal(original.StateAt("ship-a", at), restored.StateAt("ship-a", at));
        Assert.Empty(restored.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(Ticks(10000) + TimeSpan.FromDays(15).Ticks, restored.ElapsedTicks);
    }

    [Fact]
    public void CancelledLabKeepsHistoryAndDoesNotRescheduleOldArrival()
    {
        var lab = new SystemLab();
        lab.OrderTravel("planet-iris");
        lab.Advance(TimeSpan.FromMinutes(5));
        lab.CancelTravel();
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
        Assert.Equal(lab.Snapshot().Events, restored.Snapshot().Events);
        Assert.Null(restored.Snapshot().Trajectory);
        foreach (var at in new[] { Ticks(300), Ticks(21600), Ticks(40000) })
            Assert.Equal(lab.ShipStateAt(at), restored.ShipStateAt(at));
        Assert.False(restored.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(new[] { "created", "order", "cancel" }, restored.Snapshot().Events.Select(entry => entry.Kind));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-9007199254740993L)]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void SignedTickStringsAndCelestialEpochsSurviveJsonExactly(long ticks)
    {
        var state = new SystemLab().ExportState();
        var ship = state.Simulation.Spacecraft[0];
        var bodies = state.Simulation.Bodies.Add(new("moon-λ", "Test moon", CelestialBodyKind.Moon, 0, "planet-iris",
            new(12000, .1, .2, .3, .4, 1, long.MinValue),
            new(new(1, 2, 3), new(4, 5, 6)), long.MaxValue));
        state = state with
        {
            Simulation = state.Simulation with
            {
                ElapsedTicks = ticks,
                Bodies = bodies,
                Spacecraft = [ship with { FreeCoast = ship.FreeCoast! with { EpochTicks = ticks } }]
            },
            History = [state.History[0] with { AtTicks = ticks }]
        };
        var bytes = SimulationStateJson.Serialize(state);
        var json = JsonNode.Parse(bytes)!;
        Assert.Equal(ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            json["simulation"]!["elapsedTicks"]!.GetValue<string>());
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(bytes));
        var exported = restored.ExportState();
        Assert.Equal(ticks, restored.ElapsedTicks);
        Assert.Equal(ticks, exported.Simulation.Spacecraft[0].FreeCoast!.EpochTicks);
        Assert.Equal(state.History[0], exported.History[0]);
        Assert.Equal("ship-pathfinder", exported.Simulation.Spacecraft[0].Id);
        Assert.Equal(bodies[^1], Assert.Single(exported.Simulation.Bodies, body => body.Id == "moon-λ"));
        Assert.Equal(restored.CelestialPositions.GetState("moon-λ", ticks),
            SystemLab.RestoreState(exported).CelestialPositions.GetState("moon-λ", ticks));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("missing collection")]
    [InlineData("null body")]
    [InlineData("duplicate body")]
    [InlineData("nonfinite body")]
    [InlineData("unknown parent")]
    [InlineData("cyclic catalogue")]
    [InlineData("duplicate ship")]
    [InlineData("unknown primary")]
    [InlineData("unknown destination")]
    [InlineData("conflicting motion")]
    [InlineData("bad clock")]
    [InlineData("bad segment epoch")]
    [InlineData("nonfinite segment")]
    [InlineData("discontinuous segment")]
    [InlineData("missing burn")]
    [InlineData("bad burn")]
    [InlineData("bad cursor")]
    [InlineData("bad history sequence")]
    [InlineData("bad history order")]
    [InlineData("mismatched history")]
    public void InvalidRestoreLeavesOriginalLabUntouched(string corruption)
    {
        var lab = new SystemLab();
        lab.OrderTravel("planet-iris");
        lab.Advance(TimeSpan.FromHours(1));
        var state = lab.ExportState();
        var before = SimulationStateJson.Serialize(state);
        var simulation = state.Simulation;
        var body = simulation.Bodies[0];
        var ship = simulation.Spacecraft[0];
        var trajectory = ship.Trajectory!;
        var segment = trajectory.Segments[0];
        var initial = segment.Coast.InitialRelativeState;
        var badSegment = corruption switch
        {
            "bad segment epoch" => segment with { EndTicks = segment.Coast.EpochTicks },
            "nonfinite segment" => segment with
            {
                Coast = segment.Coast with
                { InitialRelativeState = initial with { PositionKm = initial.PositionKm with { X = double.NaN } } }
            },
            "discontinuous segment" => segment with
            {
                Coast = segment.Coast with
                { InitialRelativeState = initial with { PositionKm = initial.PositionKm with { X = initial.PositionKm.X + 10 } } }
            },
            _ => segment
        };
        var badTrajectory = corruption switch
        {
            "unknown destination" => trajectory with { Destination = trajectory.Destination with { BodyId = "missing" } },
            "missing burn" => trajectory with { Burns = [] },
            "bad burn" => trajectory with
            {
                Burns = trajectory.Burns.SetItem(0, trajectory.Burns[0] with
                { DeltaVelocityKmPerSecond = new(10000, 0, 0) })
            },
            "bad cursor" => trajectory with { NextBoundaryIndex = 1 },
            _ => trajectory with { Segments = [badSegment] }
        };
        var badShip = corruption switch
        {
            "unknown primary" => ship with { PrimaryBodyId = "missing" },
            "conflicting motion" => ship with { FreeCoast = new(0, initial) },
            _ => ship with { Trajectory = badTrajectory }
        };
        var badSimulation = corruption switch
        {
            "version" => simulation with { FormatVersion = SimulationState.CurrentFormatVersion + 1 },
            "missing collection" => simulation with { Bodies = default },
            "null body" => simulation with { Bodies = simulation.Bodies.SetItem(0, null!) },
            "duplicate body" => simulation with { Bodies = simulation.Bodies.Add(body) },
            "nonfinite body" => simulation with
            {
                Bodies = simulation.Bodies.SetItem(0, body with
                { GravitationalParameterKm3PerSecond2 = double.PositiveInfinity })
            },
            "unknown parent" => simulation with { Bodies = simulation.Bodies.SetItem(0, body with { ParentId = "missing" }) },
            "cyclic catalogue" => simulation with { Bodies = simulation.Bodies.SetItem(0, body with { ParentId = body.Id }) },
            "duplicate ship" => simulation with { Spacecraft = [ship, ship] },
            "bad clock" => simulation with { ElapsedTicks = trajectory.Segments[^1].EndTicks },
            _ => simulation with { Spacecraft = [badShip] }
        };
        var badState = corruption switch
        {
            "bad history sequence" => state with { LastEventSequence = 30 },
            "bad history order" => state with { History = [state.History[1], state.History[0]] },
            "mismatched history" => state with { History = [state.History[0], state.History[1] with { AtTicks = 1 }] },
            _ => state with { Simulation = badSimulation }
        };
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(badState));
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
        Assert.True(lab.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(Ticks(21600), lab.ElapsedTicks);
    }

    [Theory]
    [InlineData("numeric ticks")]
    [InlineData("overflow ticks")]
    [InlineData("missing tick")]
    [InlineData("missing nullable field")]
    [InlineData("duplicate property")]
    [InlineData("unknown property")]
    [InlineData("unsupported version")]
    [InlineData("truncated")]
    public void JsonDecoderRejectsMalformedPayloads(string corruption)
    {
        var payload = Encoding.UTF8.GetString(SimulationStateJson.Serialize(new SystemLab().ExportState()));
        var badPayload = corruption switch
        {
            "numeric ticks" => payload.Replace("\"elapsedTicks\":\"0\"", "\"elapsedTicks\":0"),
            "overflow ticks" => payload.Replace("\"elapsedTicks\":\"0\"", "\"elapsedTicks\":\"9223372036854775808\""),
            "missing tick" => payload.Replace("\"elapsedTicks\":\"0\",", ""),
            "missing nullable field" => payload.Replace(",\"rendezvous\":null", ""),
            "duplicate property" => payload.Replace("\"elapsedTicks\":\"0\"", "\"elapsedTicks\":\"0\",\"elapsedTicks\":\"0\""),
            "unknown property" => payload.Replace("\"elapsedTicks\":\"0\"", "\"elapsedTicks\":\"0\",\"preview\":0"),
            "unsupported version" => payload.Replace("\"formatVersion\":1", "\"formatVersion\":2"),
            "truncated" => payload[..^1],
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        Assert.NotEqual(payload, badPayload);
        Assert.Throws<InvalidDataException>(() => SimulationStateJson.Deserialize(Encoding.UTF8.GetBytes(badPayload)));
    }

    [Fact]
    public void LabJsonAtPatchPreservesHistoryAndRejectsARewoundBoundary()
    {
        var catalogue = PatchedCatalogue();
        var movement = new MovementExecution(catalogue);
        movement.AddSpacecraft(SystemLab.ShipId, PatchedInitial, Helios);
        var seed = new SystemLab().ExportState() with { Simulation = movement.ExportState() };
        var lab = SystemLab.RestoreState(seed);
        lab.CommitTravel(PatchedPlan((CelestialPositionService)lab.CelestialPositions, new("target")));
        Assert.True(lab.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(Ticks(10000), lab.ElapsedTicks);
        var bytes = SimulationStateJson.Serialize(lab.ExportState());
        var checkpoint = SimulationStateJson.Deserialize(bytes);
        var restored = SystemLab.RestoreState(checkpoint);
        Assert.Equal(bytes, SimulationStateJson.Serialize(restored.ExportState()));
        Assert.Equal(lab.Snapshot().Trajectory!.Points, restored.Snapshot().Trajectory!.Points);
        Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(checkpoint with
        {
            LastEventSequence = 2,
            History = checkpoint.History.RemoveAt(2)
        }));
        Assert.True(restored.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(Ticks(15000), restored.ElapsedTicks);
        Assert.Equal(new[] { "created", "order", "patch", "arrival" }, restored.Snapshot().Events.Select(entry => entry.Kind));
    }

    private static MovementExecution PatchedWorld()
    {
        var catalogue = PatchedCatalogue();
        var target = catalogue.GetState("target", Ticks(15000)).PositionKm;
        var plan = PatchedPlan(catalogue, new(null, new(target, new(1, 2, 3)), Ticks(15000)));
        var world = new MovementExecution(catalogue);
        foreach (var id in new[] { "ship-z", "ship-a" })
        {
            world.AddSpacecraft(id, PatchedInitial, Helios);
            world.Commit(id, plan);
        }
        return world;
    }

    private static CelestialPositionService PatchedCatalogue() => new([
        new(Helios, "Helios", CelestialBodyKind.Star, 1e6),
        new("planet", "Planet", CelestialBodyKind.Planet, 1000, Helios, new(100000, 0, EpochTicks: -123)),
        new("target", "Target", CelestialBodyKind.Moon, 0, "planet",
            ReferenceState: new(new(0, 2000, 100), default))
    ]);

    private static TrajectoryPlan PatchedPlan(CelestialPositionService catalogue, TrajectoryDestination target)
    {
        var soi = catalogue.GetEnvironment("planet").SphereOfInfluenceRadiusKm!.Value;
        var planetAtPatch = catalogue.GetState("planet", Ticks(10000)).PositionKm;
        var waypoint = planetAtPatch with { X = planetAtPatch.X + soi };
        return new TrajectoryPlanner(catalogue).PlanPatched(PatchedInitial, 0, [
            new(Helios, Ticks(10000), new(null, new(waypoint, default))),
            new("planet", Ticks(15000), target)
        ]);
    }

    private static void CompareBoundary(MovementExecution original, MovementExecution restored,
        TimeSpan advance, long expectedTicks, string kind)
    {
        var expected = original.Advance(advance);
        var actual = restored.Advance(advance);
        Assert.Equal(expectedTicks, restored.ElapsedTicks);
        Assert.Equal(expected, actual);
        Assert.Equal(new[] { "ship-a", "ship-z" }, actual.Select(entry => entry.SpacecraftId));
        Assert.All(actual, entry => Assert.Equal(kind, entry.Kind));
    }
}
