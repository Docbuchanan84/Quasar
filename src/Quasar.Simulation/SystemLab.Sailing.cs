namespace Quasar.Simulation;

public sealed partial class SystemLab
{
    public const string SailcraftId = "ship-sundiver";
    private const string SailcraftName = "Sundiver";

    public bool CanLaunchSailcraft => PrimaryId == SolarSystem.Sun
        && celestialPositions.Bodies.TryGetValue(SolarSystem.Earth, out var earth)
        && earth is { Orbit: not null, ParentId: SolarSystem.Sun, GravitationalParameterKm3PerSecond2: > 0 }
        && !movement.ContainsSpacecraft(SailcraftId);

    public void LaunchSailcraft()
    {
        if (!CanLaunchSailcraft) throw new InvalidOperationException("The sail flight trial is already present or unavailable in this system.");

        // Place the trial four days before a deliberately useful Earth flyby. Work backwards from
        // a safe periapsis so starting a trial at another date still uses the moving Earth.
        var encounter = checked(ElapsedTicks + 4 * TimeSpan.TicksPerDay);
        var earth = celestialPositions.GetState(SolarSystem.Earth, encounter);
        var relative = OrbitalNavigation.ToLocal(celestialPositions, SolarSystem.Sun, encounter, earth);
        var radial = Vector3d.From(relative.PositionKm);
        radial /= radial.Length;
        var velocity = Vector3d.From(relative.VelocityKmPerSecond);
        var tangent = velocity - radial * Vector3d.Dot(velocity, radial);
        tangent /= tangent.Length;
        var planet = celestialPositions.Bodies[SolarSystem.Earth];
        var sphere = celestialPositions.GetEnvironment(SolarSystem.Earth).SphereOfInfluenceRadiusKm!.Value * .99;
        var periapsisKm = planet.RadiusKm + 14_000;
        var mu = planet.GravitationalParameterKm3PerSecond2;
        var perigee = new CelestialState((tangent * -periapsisKm).Position, (radial * Math.Sqrt(25 + 2 * mu / periapsisKm)).Velocity);
        var flyby = new TwoBodyTrajectory(perigee, encounter, mu);
        var outside = ElapsedTicks;
        var inside = encounter;
        while (inside - outside > 1)
        {
            var middle = outside + (inside - outside) / 2;
            if (Vector3d.From(flyby.StateAt(middle).PositionKm).Length < sphere) inside = middle;
            else outside = middle;
        }
        var atEntry = OrbitalNavigation.ToSystem(celestialPositions, SolarSystem.Earth, outside, flyby.StateAt(outside));
        var approach = new TwoBodyTrajectory(OrbitalNavigation.ToLocal(celestialPositions, SolarSystem.Sun, outside, atEntry),
            outside, celestialPositions.GetEnvironment(SolarSystem.Sun).GravitationalParameterKm3PerSecond2);
        var initial = OrbitalNavigation.ToSystem(celestialPositions, SolarSystem.Sun, ElapsedTicks, approach.StateAt(ElapsedTicks));
        movement.AddSailcraft(SailcraftId, initial, SolarSystem.Sun, new(110, 30, 12_000));
        Record("sail-launched", "Sundiver joined the lab on an Earth flyby approach with its sail feathered.", SailcraftId);
    }

    public SailPreview PreviewSail(double? angleDegrees, int durationSeconds, CancellationToken cancellationToken = default) =>
        movement.PreviewSail(RequireSailcraft(), angleDegrees, durationSeconds, cancellationToken);

    public void StartSailTrim(string? shipId, double angleDegrees, int durationSeconds)
    {
        RequireSelectedSailcraft(shipId);
        movement.StartSailTrim(SailcraftId, angleDegrees, durationSeconds);
        Record("sail-trim", FormattableString.Invariant($"Sundiver set its sail to {angleDegrees:+0.##;-0.##;0}°."), SailcraftId);
    }

    public void FeatherSail(string? shipId)
    {
        RequireSelectedSailcraft(shipId);
        movement.FeatherSail(SailcraftId);
        Record("sail-feathered", "Sundiver feathered its sail and resumed a ballistic coast.", SailcraftId);
    }

    private string RequireSailcraft() => movement.ContainsSpacecraft(SailcraftId) ? SailcraftId
        : throw new InvalidOperationException("Launch Sundiver to use its sail controls.");

    private void RequireSelectedSailcraft(string? shipId)
    {
        if (shipId != RequireSailcraft()) throw new ArgumentException("Select Sundiver for this sail order.", nameof(shipId));
    }

    private IEnumerable<LabObject> SailObjects()
    {
        if (!movement.ContainsSpacecraft(SailcraftId)) yield break;
        var state = movement.StateAt(SailcraftId, ElapsedTicks);
        yield return SpacecraftObject(SailcraftId, SailcraftName, state);
    }

    private void RecordSailEvent(MovementEvent consequence)
    {
        var primary = celestialPositions.Bodies[movement.PrimaryOf(SailcraftId)];
        Record(consequence.Kind, consequence.Kind == "sail-complete"
            ? "Sundiver completed the trim, feathered its sail and resumed a ballistic coast."
            : $"Sundiver changed gravity primary to {primary.Name} while coasting.", SailcraftId);
    }
}
