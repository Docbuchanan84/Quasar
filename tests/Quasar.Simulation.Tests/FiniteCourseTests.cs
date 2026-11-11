using Quasar.Simulation;

namespace Quasar.Simulation.Tests;

public class FiniteCourseTests
{
    [Fact]
    public void InstalledEngineAndFuelChangeTheRoutesFound()
    {
        var baseline = Fastest(SystemLab.CreateChemicalFixture());
        var higherThrust = Fastest(WithShip(ship => ship with
        {
            Propulsion = ship.Propulsion! with { Engine = ship.Propulsion.Engine with { ThrustN = 400_000 } }
        }));
        var lowThrust = WithShip(ship => ship with
        {
            Propulsion = ship.Propulsion! with { Engine = ship.Propulsion.Engine with { ThrustN = 0.001 } }
        });
        var halfFuel = Fastest(WithFuel(5000));
        var dry = Find(WithFuel(0));

        Assert.NotEqual(baseline.Finite!.State.DepartureDurationTicks, higherThrust.Finite!.State.DepartureDurationTicks);
        Assert.True(halfFuel.TravelSeconds > baseline.TravelSeconds);
        Assert.Null(Find(lowThrust).Choices);
        Assert.Contains(Find(lowThrust).InfeasibleReasons, reason => reason.Contains("supported duration"));
        Assert.Null(dry.Choices);
        Assert.Contains("No propellant", Assert.Single(dry.InfeasibleReasons));
    }

    [Fact]
    public void CourseUsesFiniteBurnsAndRestoresEachPhaseWithoutAnArrivalImpulse()
    {
        var lab = SystemLab.CreateChemicalFixture();
        var course = Fastest(lab);
        var finite = course.Finite!;
        var initial = lab.ShipStateAt(0);
        lab.CommitTravel(course);
        Assert.Equal(initial, lab.ShipStateAt(0));
        var afterArrivalTicks = course.ArrivalTicks + TimeSpan.FromDays(1).Ticks;
        var predictedCoast = lab.TargetStateAt("spacecraft", SystemLab.ShipId, afterArrivalTicks);

        AdvanceTo(lab, finite.Departure.StartTicks + finite.State.DepartureDurationTicks / 2);
        Assert.InRange(lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 0, 9999);
        var restored = RoundTrip(lab);
        var cutoff = finite.Departure.EndTicks;
        Assert.True(lab.Advance(TimeSpan.FromDays(30)));
        AdvanceTo(restored, cutoff);
        Assert.Equal("course-departure-cutoff", lab.LastEventKind);
        Assert.Equal(lab.LastEvent, restored.LastEvent);
        restored = RoundTrip(restored);

        AdvanceTo(lab, finite.Braking.StartTicks);
        AdvanceTo(restored, finite.Braking.StartTicks);
        Assert.Equal("course-braking-start", restored.LastEventKind);
        AdvanceTo(restored, finite.Braking.StartTicks + finite.State.BrakingDurationTicks / 2);
        restored = RoundTrip(restored);

        Assert.True(lab.Advance(TimeSpan.FromDays(30)));
        Assert.True(restored.Advance(TimeSpan.FromDays(30)));
        Assert.Equal(course.ArrivalTicks, restored.ElapsedTicks);
        Assert.Equal("course-approach-complete", restored.LastEventKind);
        Assert.Equal(course.StateAt(course.ArrivalTicks), restored.ShipStateAt(restored.ElapsedTicks));
        Assert.Equal(lab.ShipStateAt(lab.ElapsedTicks), restored.ShipStateAt(restored.ElapsedTicks));
        Assert.Equal(finite.RemainingPropellantKg, restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg);
        Assert.Equal(predictedCoast, restored.TargetStateAt("spacecraft", SystemLab.ShipId, afterArrivalTicks));
        Assert.Null(restored.Snapshot().Trajectory);
        Assert.Equal("Coasting", RoundTrip(restored).Snapshot().Ship.Status);
        AssertOlderV6ArrivalRestores(restored, "Pathfinder completed braking at the approach point and resumed a gravitational coast.");
    }

    [Fact]
    public void CancellingOrReplanningMidburnUsesCurrentVelocityAndFuel()
    {
        var lab = SystemLab.CreateChemicalFixture();
        var plan = Find(lab).Choices!.LowestManeuverCost;
        lab.CommitTravel(plan);
        AdvanceTo(lab, plan.Finite!.State.DepartureDurationTicks / 2);
        var state = lab.ShipStateAt(lab.ElapsedTicks);
        var fuel = lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg;
        var replanned = Fastest(lab);
        Assert.Equal(state, replanned.InitialState);
        Assert.Equal(fuel, replanned.Finite!.State.Propulsion.Tank.QuantityKg);
        lab.CommitTravel(replanned);
        Assert.Equal(state, lab.ShipStateAt(lab.ElapsedTicks));
        lab.CancelTravel();
        Assert.Equal(state, lab.ShipStateAt(lab.ElapsedTicks));
        Assert.Equal(fuel, lab.Snapshot().Ship.Propulsion!.AvailablePropellantKg);
        Assert.Null(RoundTrip(lab).Snapshot().Trajectory);
    }

    [Theory]
    [InlineData("departure-start")]
    [InlineData("departure-cutoff")]
    [InlineData("coast")]
    [InlineData("braking-start")]
    [InlineData("braking")]
    [InlineData("last-powered-tick")]
    public void RestoringThenCancellingPreservesStateAndFuelAndDropsFutureBurns(string phase)
    {
        var lab = SystemLab.CreateChemicalFixture();
        var plan = Fastest(lab);
        var finite = plan.Finite!;
        var departureEnd = finite.Departure.EndTicks;
        var brakingStart = finite.Braking.StartTicks;
        var at = phase switch
        {
            "departure-start" => plan.DepartureTicks,
            "departure-cutoff" => departureEnd,
            "coast" => departureEnd + (brakingStart - departureEnd) / 2,
            "braking-start" => brakingStart,
            "braking" => brakingStart + (plan.ArrivalTicks - brakingStart) / 2,
            "last-powered-tick" => plan.ArrivalTicks - 1,
            _ => throw new ArgumentException(nameof(phase))
        };
        lab.CommitTravel(plan);
        while (lab.ElapsedTicks < at) AdvanceTo(lab, at);

        var state = lab.ShipStateAt(at);
        var flow = finite.State.Propulsion.CalculatePerformance().RatedMassFlowKgPerSecond;
        var firedTicks = Math.Min(at, departureEnd) - plan.DepartureTicks + Math.Max(0, at - brakingStart);
        var expectedFuel = finite.State.Propulsion.Tank.QuantityKg - flow * (firedTicks / (double)TimeSpan.TicksPerSecond);
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(lab.ExportState())));
        Assert.Equal(plan.StateAt(at), restored.ShipStateAt(at));
        Assert.Equal(expectedFuel, restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 8);

        restored.CancelTravel();
        Assert.Equal(state, restored.ShipStateAt(at));
        Assert.Equal(expectedFuel, restored.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 8);
        Assert.True(restored.ExportState().Simulation.Spacecraft[0].FreeCoast!.AutomaticPrimary);
        var afterCancel = RoundTrip(restored);
        var events = afterCancel.Snapshot().Events.Length;
        while (afterCancel.ElapsedTicks <= plan.ArrivalTicks) AdvanceTo(afterCancel, plan.ArrivalTicks + 1);
        Assert.DoesNotContain(afterCancel.Snapshot().Events.Skip(events), entry => entry.Kind.StartsWith("course-", StringComparison.Ordinal));
        Assert.Equal(expectedFuel, afterCancel.Snapshot().Ship.Propulsion!.AvailablePropellantKg, 8);
    }

    [Fact]
    public void SamePrimaryOrbitRequestEndsInABoundOrbit()
    {
        var lab = WithShip(ship =>
        {
            const double radius = 6800;
            var speed = Math.Sqrt(398600.435 / radius);
            return ship with
            {
                PrimaryBodyId = SolarSystem.Earth,
                FreeCoast = new(0, new(new(radius, 0, 0), new(0, speed, 0)))
            };
        });
        var plan = lab.CaptureCourseSearch(SolarSystem.Earth, arrivalKind: "orbit")(CancellationToken.None).Choices!.Fastest;
        var finite = plan.Finite!;
        Assert.Equal("orbit", finite.Arrival.Kind);
        Assert.Equal(SolarSystem.Earth, finite.Arrival.PrimaryBodyId);
        Assert.True(finite.ArrivalCoast.PeriapsisKm > 6378);
        Assert.InRange(finite.Arrival.PositionErrorKm, 0, FiniteCourse.PositionToleranceKm);
        Assert.InRange(finite.Arrival.VelocityErrorKmPerSecond, 0, FiniteCourse.VelocityToleranceKmPerSecond);

        lab.CommitTravel(plan);
        while (lab.ElapsedTicks < plan.ArrivalTicks) AdvanceTo(lab, plan.ArrivalTicks);
        var achieved = RoundTrip(lab);
        Assert.Contains("entered the requested orbit", achieved.LastEvent.Message);
        Assert.Equal("orbit", achieved.Snapshot().Ship.ArrivalKind);
        Assert.Equal("Orbital insertion complete", achieved.LastEvent.ArrivalLabel);
        var position = achieved.ShipStateAt(plan.ArrivalTicks).PositionKm;
        var earth = achieved.CelestialPositions.GetState(SolarSystem.Earth, plan.ArrivalTicks).PositionKm;
        Assert.Equal(finite.Arrival.TargetRadiusKm, position.DistanceTo(earth), 2);
        Assert.Equal(SolarSystem.Earth, achieved.TargetStateAt("spacecraft", SystemLab.ShipId, plan.ArrivalTicks).ReferenceBodyId);
        AssertOlderV6ArrivalRestores(achieved, "Pathfinder completed braking and entered the requested orbit.");
    }

    private static void AssertOlderV6ArrivalRestores(SystemLab lab, string oldMessage)
    {
        var state = lab.ExportState();
        var old = state.History[^1] with { Kind = "course-arrival", Message = oldMessage };
        var legacy = state with { History = state.History.SetItem(state.History.Length - 1, old) };
        var restored = SystemLab.RestoreState(SimulationStateJson.Deserialize(SimulationStateJson.Serialize(legacy)));
        Assert.Equal(lab.LastEventKind, restored.LastEventKind);
        Assert.Equal(lab.LastEvent.ArrivalLabel, restored.LastEvent.ArrivalLabel);
        Assert.Equal(lab.Snapshot().Ship.ArrivalKind, restored.Snapshot().Ship.ArrivalKind);
    }

    private static CourseSearchResult Find(SystemLab lab) => lab.CaptureCourseSearch(SolarSystem.Earth)(CancellationToken.None);
    private static TrajectoryPlan Fastest(SystemLab lab) => Assert.IsType<CourseChoices>(Find(lab).Choices).Fastest;
    private static SystemLab RoundTrip(SystemLab lab) => SystemLab.RestoreState(lab.ExportState());
    private static void AdvanceTo(SystemLab lab, long ticks) => lab.Advance(TimeSpan.FromTicks(ticks - lab.ElapsedTicks));
    private static SystemLab WithFuel(double fuel) => WithShip(ship => ship with
    {
        Propulsion = ship.Propulsion! with { Tank = ship.Propulsion.Tank with { QuantityKg = fuel } }
    });

    private static SystemLab WithShip(Func<SpacecraftState, SpacecraftState> transform)
    {
        var state = SystemLab.CreateChemicalFixture().ExportState();
        return SystemLab.RestoreState(state with { Simulation = state.Simulation with { Spacecraft = [transform(state.Simulation.Spacecraft[0])] } });
    }
}
