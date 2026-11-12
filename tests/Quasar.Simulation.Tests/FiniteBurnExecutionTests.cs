using Quasar.Server;

namespace Quasar.Simulation.Tests;

public sealed class FiniteBurnExecutionTests
{
    [Theory]
    [InlineData(30, 30, "burn-complete")]
    [InlineData(3600, 150, "burn-exhausted")]
    public void MidBurnRestoreAndPartitionedAdvanceReachExactlyTheSameBoundary(int requested, int cutoff, string eventKind)
    {
        var lab = SystemLab.CreateChemicalFixture();
        lab.StartBurn("prograde", requested);
        var endTicks = TimeSpan.FromSeconds(cutoff).Ticks;
        var endState = lab.ShipStateAt(endTicks);
        var future = lab.ShipStateAt(endTicks + TimeSpan.TicksPerMinute);
        var partitioned = Restore(lab);

        for (var i = 0; i < 7; i++)
            Assert.False(partitioned.Advance(TimeSpan.FromSeconds(1)));
        var restored = Restore(partitioned);
        Assert.Equal(partitioned.Snapshot().Ship, restored.Snapshot().Ship);
        Assert.Equal(partitioned.ShipStateAt(partitioned.ElapsedTicks), restored.ShipStateAt(restored.ElapsedTicks));
        Assert.InRange(restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 9_500, 9_600);

        foreach (var world in new[] { lab, partitioned, restored })
        {
            Assert.True(world.Advance(TimeSpan.FromDays(30)));
            Assert.Equal(endTicks, world.ElapsedTicks);
            Assert.Equal(eventKind, world.LastEventKind);
            Assert.Equal("Coasting", world.Snapshot().Ship.Status);
            Assert.Null(world.Snapshot().Ship.BurnEndTicks);
            Assert.Equal(endState, world.ShipStateAt(endTicks));
            Assert.Equal(future, world.ShipStateAt(endTicks + TimeSpan.TicksPerMinute));
            var fuel = world.Snapshot().Ship.Propulsion!.AvailablePropellantKg;
            Assert.Equal(eventKind == "burn-exhausted" ? 0 : 8000, fuel, 8);
            Assert.False(world.Advance(TimeSpan.FromMinutes(1)));
            Assert.Equal(fuel, world.Snapshot().Ship.Propulsion!.AvailablePropellantKg);
            Assert.Single(world.Snapshot().Events, entry => entry.Kind == eventKind);
            Assert.Equal(world.Snapshot().Ship, Restore(world).Snapshot().Ship);
        }

        Assert.Equal(SimulationStateJson.Serialize(lab.ExportState()), SimulationStateJson.Serialize(restored.ExportState()));
    }

    [Fact]
    public void ManualCutoffPreservesMomentumAndInvalidatesTheOldBoundary()
    {
        var lab = SystemLab.CreateChemicalFixture();
        var oldPreview = lab.PreviewTravel("planet-earth", 21_600, 100);
        lab.StartBurn("retrograde", 30);
        Assert.Throws<InvalidOperationException>(() => lab.CommitTravel(oldPreview));
        Assert.Throws<InvalidOperationException>(() => lab.PreviewTravel("planet-earth"));
        Assert.False(lab.Advance(TimeSpan.FromSeconds(5)));
        var atCutoff = lab.ShipStateAt(lab.ElapsedTicks);
        var fuel = lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg;
        lab.CutThrust();
        Assert.Equal(atCutoff, lab.ShipStateAt(lab.ElapsedTicks));
        Assert.False(lab.Advance(TimeSpan.FromSeconds(30)));
        Assert.Equal(fuel, lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg);
        Assert.Equal("burn-cutoff", lab.LastEventKind);
        lab.CommitTravel(lab.PreviewTravel("planet-earth", 21_600, 100));
        Assert.Throws<InvalidOperationException>(() => lab.StartBurn("prograde", 30));
        Assert.Equal("Underway", Restore(lab).Snapshot().Ship.Status);
    }

    [Fact]
    public void CheckpointRejectsContradictoryBurnDataAndReadsVersionThree()
    {
        var lab = SystemLab.CreateChemicalFixture();
        var coast = lab.ExportState();
        Assert.Equal(coast.Simulation.Spacecraft[0].Propulsion,
            SystemLab.RestoreState(coast with
            {
                Simulation = coast.Simulation with
                {
                    FormatVersion = 3,
                    Spacecraft = [coast.Simulation.Spacecraft[0] with
                    {
                        FreeCoast = coast.Simulation.Spacecraft[0].FreeCoast! with { AutomaticPrimary = false }
                    }]
                }
            })
                .ExportState().Simulation.Spacecraft[0].Propulsion);
        lab.StartBurn("prograde", 30);
        lab.Advance(TimeSpan.FromSeconds(5));
        var state = lab.ExportState();
        var simulation = state.Simulation;
        var ship = simulation.Spacecraft[0];
        var wrongFuel = ship.Propulsion! with { Tank = ship.Propulsion.Tank with { QuantityKg = 10_000 } };
        foreach (var invalid in new[]
        {
            simulation with { FormatVersion = 3 },
            simulation with { Spacecraft = [ship with { Propulsion = wrongFuel }] },
            simulation with { Spacecraft = [ship with { FreeCoast = coast.Simulation.Spacecraft[0].FreeCoast }] },
            simulation with { Spacecraft = [ship with { ActiveBurn = ship.ActiveBurn! with { StartTicks = 1 } }] },
            simulation with { ElapsedTicks = TimeSpan.FromSeconds(30).Ticks },
        })
            Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(state with { Simulation = invalid }));
    }

    [Theory]
    [InlineData("continuous", 30, 30, "burn-complete")]
    [InlineData("once", 3600, 150, "burn-exhausted")]
    public void HostSpendsTheFullIntervalAcrossBurnEndAndRejectsStaleCommands(string mode, int burnSeconds,
        int cutoffSeconds, string kind)
    {
        var session = new LabSession(SystemLab.CreateChemicalFixture());
        var client = Guid.NewGuid().ToString();
        var initial = session.Snapshot();
        LabCommand Command(SessionSnapshot snapshot, string operation) =>
            new(snapshot.InstanceId, snapshot.Revision, client, operation);
        var burn = Command(initial, "start-burn") with { BurnDirection = "prograde", BurnSeconds = burnSeconds };
        var started = session.Execute(burn);
        Assert.Throws<InvalidOperationException>(() => session.Execute(burn));
        var configured = session.Execute(Command(started, "configure") with { DurationSeconds = 3600, Mode = mode });
        session.Execute(Command(configured, "play"));
        Assert.Null(session.TickWithArrival());
        var advanced = session.Snapshot();
        Assert.Equal(mode == "continuous", advanced.Running);
        Assert.Equal(TimeSpan.TicksPerHour, advanced.World.ElapsedTicks);
        Assert.Equal(mode == "continuous" ? "Processing" : "Advance complete", advanced.StopReason);
        Assert.Null(advanced.Interrupt);
        var notice = Assert.Single(advanced.World.Events, e => e.Kind == kind);
        Assert.Equal(TimeSpan.FromSeconds(cutoffSeconds).Ticks, notice.AtTicks);
        Assert.Equal(notice, advanced.FlightNotice);
    }

    private static SystemLab Restore(SystemLab lab) =>
        SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
}
