#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:project ../src/Quasar.Simulation/Quasar.Simulation.csproj
using System.Diagnostics;
using Quasar.Simulation;

const string sun = "star-sun";
var catalogue = (CelestialPositionService)new SystemLab().CelestialPositions;
var earth = catalogue.GetState("planet-earth", 0).PositionKm;
var earthDistance = Math.Sqrt(earth.X * earth.X + earth.Y * earth.Y + earth.Z * earth.Z);
var mu = catalogue.GetEnvironment(sun).GravitationalParameterKm3PerSecond2;
Run(1);
foreach (var count in args.Length == 0 ? new[] { 1, 100, 1000 } : args.Select(int.Parse))
{
    var samples = new double[3];
    for (var i = 0; i < samples.Length; i++) samples[i] = Run(count);
    Array.Sort(samples);
    Console.WriteLine($"{count,4} ships: {samples[1]:F3} ms median ({samples[0]:F3}–{samples[2]:F3} ms)");
}

double Run(int count)
{
    var movement = new MovementExecution(catalogue);
    for (var i = 0; i < count; i++)
    {
        // Distinct circular heliocentric coasts with radial overlap, starting away from Earth.
        var radius = earthDistance + 500_000 + i * 100;
        var phase = Math.Atan2(earth.Y, earth.X) + 1 + i * 0.0001;
        var (sin, cos) = Math.SinCos(phase);
        var speed = Math.Sqrt(mu / radius);
        movement.AddSpacecraft($"ship-{i}", new(new(radius * cos, radius * sin, 0),
            new(-speed * sin, speed * cos, 0)), sun, automaticPrimary: true);
    }
    var watch = Stopwatch.StartNew();
    var events = movement.Advance(TimeSpan.FromDays(30));
    watch.Stop();
    if (events.Length != 0 || movement.ElapsedTicks != 30 * TimeSpan.TicksPerDay)
        throw new InvalidOperationException("The benchmark fixture must advance the full 30 days without events.");
    return watch.Elapsed.TotalMilliseconds;
}
