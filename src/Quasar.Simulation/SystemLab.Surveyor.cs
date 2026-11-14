namespace Quasar.Simulation;

public sealed partial class SystemLab
{
    public const string SurveyorId = "ship-surveyor";

    public bool CanLaunchSurveyor => PrimaryId == SolarSystem.Sun
        && celestialPositions.Bodies.TryGetValue(SolarSystem.Earth, out var earth)
        && earth is { Orbit: not null, ParentId: SolarSystem.Sun, GravitationalParameterKm3PerSecond2: > 0 }
        && !movement.ContainsSpacecraft(SurveyorId);

    public void LaunchSurveyor()
    {
        if (!CanLaunchSurveyor) throw new InvalidOperationException("Surveyor is already present or unavailable in this system.");
        var earth = celestialPositions.GetState(SolarSystem.Earth, ElapsedTicks);
        var radial = Vector3d.From(earth.PositionKm);
        var distance = celestialPositions.GetEnvironment(SolarSystem.Earth).SphereOfInfluenceRadiusKm
            ?? throw new InvalidOperationException("Surveyor needs Earth's modeled gravity sphere.");
        var startingPosition = radial + radial / radial.Length * distance * 2;
        var propulsion = new SpacecraftPropulsion(9000, null, new(1000, 6000, 6000, "argon"),
            new(4000, 7, 2, 10, .6, 40_000, "argon"));
        movement.AddSpacecraft(SurveyorId, new(startingPosition.Position, earth.VelocityKmPerSecond),
            SolarSystem.Sun, propulsion, automaticPrimary: true);
        Record("surveyor-launched", "Surveyor joined the lab in solar orbit near Earth with its fission-electric drive.", SurveyorId);
    }

    private IEnumerable<LabObject> SurveyorObjects()
    {
        if (!movement.ContainsSpacecraft(SurveyorId)) yield break;
        var view = movement.View(SurveyorId);
        yield return new LabObject(view.SpacecraftId, "Surveyor", "spacecraft", view.State.PositionKm, null,
            view.State.VelocityKmPerSecond)
        { Navigation = movement.NavigationOf(SurveyorId), ShipStatus = PoweredShipStatus(view) };
    }

    private ShipStatus PoweredShipStatus(MovementView view)
    {
        var id = view.SpacecraftId;
        var plan = movement.CommittedPlan(id);
        var distance = view.DestinationId is { } target
            ? view.State.PositionKm.DistanceTo(ArrivalPosition(target)) : 0;
        var electric = movement.PropulsionOf(id)?.ElectricDrive;
        return new(view.Status, Vector3d.From(view.State.VelocityKmPerSecond).Length, view.DestinationId, view.ArrivalTicks,
            distance, view.Propulsion, view.BurnEndTicks,
            (id == ShipId ? display?.ArrivalKind : plan?.Finite?.Arrival.Kind)
                ?? events.LastOrDefault(entry => entry.ObjectId == id)?.ArrivalKind,
            electric is null ? null : new(electric.ElectricalPowerMw, electric.Efficiency, electric.ThrustN, electric.ExhaustVelocityMPerSecond));

        Position3 ArrivalPosition(string target)
        {
            if (id == ShipId && display?.Arrival is not null) return display.Points[^1].PositionKm;
            if (plan?.Finite is { } finite)
                return OrbitalNavigation.ToSystem(celestialPositions, finite.Arrival.PrimaryBodyId, plan.ArrivalTicks,
                    finite.ArrivalCoast.InitialState).PositionKm;
            return celestialPositions.GetState(target, ElapsedTicks).PositionKm;
        }
    }

    private void RequirePoweredShip(string shipId)
    {
        if (shipId is not (ShipId or SurveyorId) || !movement.ContainsSpacecraft(shipId))
            throw new ArgumentException("Choose a spacecraft with a propellant-driven engine.", nameof(shipId));
    }

    public static string PoweredShipName(string shipId) => shipId switch
    {
        ShipId => "Pathfinder",
        SurveyorId => "Surveyor",
        _ => shipId
    };
}
