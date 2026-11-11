using System.Diagnostics;

namespace Quasar.Simulation.Tests;

[Collection("Patched course integration")]
public sealed class PatchedCourseTests
{
    [Fact]
    public void CallistoExecutionRestoresAndCancelsOnBothSidesOfEveryBoundary()
    {
        var lab = new SystemLab();
        var before = SimulationStateJson.Serialize(lab.ExportState());
        var plan = Callisto(lab);
        Assert.Equal(before, SimulationStateJson.Serialize(lab.ExportState()));
        var finite = plan.Finite!;
        Assert.Contains(finite.Boundaries, boundary => boundary.Kind == "primary-change");
        Assert.Equal("planet-jupiter", finite.Arrival.PrimaryBodyId);
        Assert.InRange(finite.Arrival.PositionErrorKm, 0, FiniteCourse.PositionToleranceKm);
        Assert.InRange(finite.Arrival.VelocityErrorKmPerSecond, 0, FiniteCourse.VelocityToleranceKmPerSecond);
        lab.CommitTravel(plan);
        var samples = finite.Boundaries.SelectMany(boundary => new[] { boundary.Ticks - 1, boundary.Ticks })
            .Append(finite.Departure.StartTicks + finite.State.DepartureDurationTicks / 2)
            .Distinct().Order().ToArray();
        foreach (var ticks in samples)
        {
            while (lab.ElapsedTicks < ticks) lab.Advance(TimeSpan.FromTicks(ticks - lab.ElapsedTicks));
            var saved = SimulationStateJson.Serialize(lab.ExportState());
            var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(saved));
            Assert.Equal(saved, SimulationStateJson.Serialize(restored.ExportState()));
            Assert.Equal(plan.StateAt(ticks), restored.ShipStateAt(ticks));
            Assert.Equal(finite.PropellantAt(ticks), restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 7);
            if (ticks == plan.ArrivalTicks) continue;
            var state = restored.ShipStateAt(ticks);
            var fuel = restored.Snapshot().Ship.Propulsion;
            restored.CancelTravel();
            Assert.Equal(state, restored.ShipStateAt(ticks));
            Assert.Equal(fuel, restored.Snapshot().Ship.Propulsion);
            Assert.Null(restored.Snapshot().Trajectory);
            Assert.Equal(restored.Snapshot().Ship, SystemLab.RestoreState(restored.ExportState()).Snapshot().Ship);
        }
        Assert.Equal("course-approach-complete", lab.LastEventKind);
        Assert.Equal(20_000 - plan.Fuel.PropellantMassKg!.Value, lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 7);
        Assert.Equal("approach", lab.Snapshot().Ship.ArrivalKind);
        var back = lab.CaptureCourseSearch("planet-earth")(CancellationToken.None);
        Assert.NotNull(back.Choices);
    }

    [Theory]
    [InlineData("moon-callisto", "planet-earth")]
    [InlineData("planet-earth", "moon-callisto")]
    [InlineData("moon-moon", "planet-mars")]
    [InlineData("planet-jupiter", "moon-europa")]
    [InlineData("moon-callisto", "moon-europa")]
    [InlineData("star-sun", "moon-titan")]
    [InlineData("star-sun", "dwarfplanet-pluto")]
    [InlineData("star-sun", "station-staryard")]
    [InlineData("star-sun", "star-sun")]
    public void ComputerFindsRoutesFromDifferentGravitySystems(string origin, string destination)
    {
        var lab = InOrbit(origin);
        var watch = Stopwatch.StartNew();
        var result = lab.CaptureCourseSearch(destination)(CancellationToken.None);
        Assert.NotNull(result.Choices);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0, 8);
        var plan = result.Choices.Fastest;
        Assert.True(plan.UsesFiniteBurns);
        Assert.Equal(destination, plan.TargetBodyId);
        lab.CommitTravel(plan);
        while (lab.ElapsedTicks < plan.ArrivalTicks) lab.Advance(TimeSpan.FromTicks(plan.ArrivalTicks - lab.ElapsedTicks));
        Assert.Equal("course-approach-complete", lab.LastEventKind);
        Assert.InRange(lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 0.001, 20_000);
    }

    [Fact]
    public void SliderRecalculationRetainsTheReserveAndHonorsTheTimeLock()
    {
        var lab = new SystemLab();
        var initial = Callisto(lab);
        var seconds = (int)initial.TravelSeconds;
        var adjusted = lab.CaptureCourseSearch("moon-callisto", preferredFlightSeconds: seconds,
            reservePercent: 95)(CancellationToken.None).Choices;
        Assert.NotNull(adjusted);
        Assert.InRange(adjusted.Tuned!.Fuel.PropellantMassKg!.Value, 0, 1000);
        var exact = lab.CaptureCourseSearch("moon-callisto", preferredFlightSeconds: seconds,
            lockFlightTime: true)(CancellationToken.None).Choices;
        Assert.Equal(seconds, exact!.Tuned!.TravelSeconds);
        var impossible = lab.CaptureCourseSearch("moon-callisto", preferredFlightSeconds: 1,
            reservePercent: 95, lockFlightTime: true)(CancellationToken.None);
        Assert.Null(impossible.Choices);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => lab.CaptureCourseSearch("moon-callisto")(cancelled.Token));
    }

    [Fact]
    public void ReserveAdjustmentFromAFastCallistoPreviewFindsAProtectedRoute()
    {
        var lab = new SystemLab();
        var fastest = lab.CaptureCourseSearch("moon-callisto")(CancellationToken.None).Choices!.Fastest;
        var protectedRoute = lab.CaptureCourseSearch("moon-callisto", preferredFlightSeconds: (int)fastest.TravelSeconds,
            reservePercent: 95)(CancellationToken.None).Choices;
        Assert.NotNull(protectedRoute);
        Assert.InRange(protectedRoute.Tuned!.Fuel.PropellantMassKg!.Value, 0, 1000);
        Assert.True(protectedRoute.Tuned.TravelSeconds > fastest.TravelSeconds);
    }

    [Fact]
    public void PatchedCheckpointsRejectWrongFuelPrimaryThrottleAndOldFormat()
    {
        var lab = new SystemLab();
        lab.CommitTravel(Callisto(lab));
        var state = lab.ExportState();
        var ship = state.Simulation.Spacecraft[0];
        var course = ship.FiniteCourse!;
        foreach (var invalid in new[]
        {
            state.Simulation with { FormatVersion = 6 },
            state.Simulation with { Spacecraft = [ship with { PrimaryBodyId = "planet-earth" }] },
            state.Simulation with { Spacecraft = [ship with { FiniteCourse = course with { DepartureThrottle = 1.01 } }] },
            state.Simulation with { Spacecraft = [ship with { Propulsion = ship.Propulsion! with { Tank = ship.Propulsion.Tank with { QuantityKg = 19999 } } }] },
        }) Assert.Throws<InvalidDataException>(() => SystemLab.RestoreState(state with { Simulation = invalid }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void PriorCheckpointVersionsKeepTheirCoastAndEquipment(int version)
    {
        var state = SystemLab.CreateChemicalFixture().ExportState();
        var ship = state.Simulation.Spacecraft[0];
        ship = ship with
        {
            Propulsion = version < 3 ? null : ship.Propulsion,
            FreeCoast = ship.FreeCoast! with { AutomaticPrimary = version >= 5 }
        };
        var restored = SystemLab.RestoreState(state with { Simulation = state.Simulation with { FormatVersion = version, Spacecraft = [ship] } });
        Assert.Equal(ship.Propulsion, restored.ExportState().Simulation.Spacecraft[0].Propulsion);
        Assert.Equal(ship.FreeCoast.InitialRelativeState.ToRuntime(),
            OrbitalNavigation.ToLocal(SolarSystem.CreateCatalogue(), ship.PrimaryBodyId, 0, restored.ShipStateAt(0)));
    }

    private static TrajectoryPlan Callisto(SystemLab lab) => Assert.IsType<CourseChoices>(lab.CaptureCourseSearch("moon-callisto",
        preferredFlightSeconds: 240 * 86400, lockFlightTime: true)(CancellationToken.None).Choices).Fastest;

    private static SystemLab InOrbit(string primary)
    {
        var lab = new SystemLab();
        if (primary == "star-sun") return lab;
        var state = lab.ExportState();
        var body = SolarSystem.CreateCatalogue().Bodies[primary];
        var radius = body.RadiusKm + 400;
        var local = new CelestialState(new(radius, 0, 0), new(0, Math.Sqrt(body.GravitationalParameterKm3PerSecond2 / radius), 0));
        var ship = state.Simulation.Spacecraft[0] with { PrimaryBodyId = primary, FreeCoast = new(0, KinematicState.From(local), true) };
        return SystemLab.RestoreState(state with { Simulation = state.Simulation with { Spacecraft = [ship] } });
    }
}

[CollectionDefinition("Patched course integration", DisableParallelization = true)]
public sealed class PatchedCourseIntegrationCollection;
