#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property AssemblyName=Quasar.Benchmarks
#:project ../src/Quasar.Simulation/Quasar.Simulation.csproj
using System.Diagnostics;
using System.Runtime.InteropServices;
using Quasar.Simulation;

const int legs = 3;
const string staryard = "station-staryard";
const string habitat = "station-oneill";
var count = args.Length > 0 ? int.Parse(args[0]) : 1_000;
var workers = args.Length > 1 ? int.Parse(args[1]) : 1;
var repetitions = args.Length > 2 ? int.Parse(args[2]) : 3;
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetitions);

Console.WriteLine($"{RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}; "
    + $"{count:N0} ships, {legs} legs each, {workers} planner workers, {repetitions} samples");
Run(Math.Min(32, count), 12);
for (var repetition = 1; repetition <= repetitions; repetition++)
{
    var result = Run(count, 1_000);
    Console.WriteLine($"Run {repetition}: {result.Arrivals:N0} arrivals, {result.Events:N0} events in {result.Advances:N0} advances, "
        + $"{result.FinalTick / (double)TimeSpan.TicksPerDay:F5} simulated days, "
        + $"{result.Queries:N0} position/velocity queries, checksum {result.Checksum:R}");
    Print("Plan", result.Plan);
    Print("Commit", result.Commit);
    Print("Advance", result.Advance);
    Print("Query", result.Query);
}

void Print(string label, Measurement measurement) => Console.WriteLine(
    $"  {label,-7} {measurement.Milliseconds,10:F3} ms  {measurement.Bytes / 1_048_576d,9:F2} MiB allocated");

FleetResult Run(int shipCount, int snapshots)
{
    var catalogue = SolarSystem.CreateCatalogue();
    var movement = new MovementExecution(catalogue);
    var lab = new SystemLab(movement);
    var origin = catalogue.GetState(staryard, 0);
    var propulsion = SystemLab.ExplorationTorch();
    var ids = Enumerable.Range(0, shipCount).Select(index => $"ship-{index:D6}").ToArray();
    var shipIndices = ids.Select((id, index) => (id, index)).ToDictionary(ship => ship.id, ship => ship.index, StringComparer.Ordinal);
    for (var i = 0; i < ids.Length; i++)
    {
        var position = origin.PositionKm with { Z = origin.PositionKm.Z + 10 + i * 0.1 };
        movement.AddSpacecraft(ids[i], origin with { PositionKm = position }, SolarSystem.Earth, propulsion, automaticPrimary: true);
    }

    var plan = new Measurement();
    var commit = new Measurement();
    var advance = new Measurement();
    var query = new Measurement();
    var checksum = 0d;
    var events = 0;
    var arrivals = 0;
    var advances = 0;
    var options = new ParallelOptions { MaxDegreeOfParallelism = workers };

    for (var leg = 0; leg < legs; leg++)
    {
        var target = leg % 2 == 0 ? habitat : staryard;
        var departure = movement.ElapsedTicks;
        var plans = new TrajectoryPlan[ids.Length];
        var searches = new Func<CancellationToken, CourseSearchResult>[ids.Length];
        plan += Measure(() =>
        {
            // Capture on the movement writer. Only detached searches run on workers.
            for (var i = 0; i < ids.Length; i++)
                searches[i] = lab.CaptureCourseSearchForSpacecraft(ids[i], target,
                    preferredFlightSeconds: 12 * 3600 + i % 3600, lockFlightTime: true);
            if (workers == 1)
                for (var i = 0; i < ids.Length; i++) Solve(i);
            else
                Parallel.For(0, ids.Length, options, Solve);
        });

        void Solve(int index)
        {
            var result = searches[index](CancellationToken.None);
            plans[index] = result.Choices?.Tuned ?? throw new InvalidOperationException(
                $"Leg {leg + 1}, {ids[index]} to {target}: {string.Join("; ", result.InfeasibleReasons)}");
        }

        commit += Measure(() =>
        {
            for (var i = 0; i < ids.Length; i++) movement.Commit(ids[i], plans[i]);
        });

        var firstArrival = plans.Min(route => route.ArrivalTicks);
        var lastArrival = plans.Max(route => route.ArrivalTicks);
        var samples = snapshots / legs + (leg < snapshots % legs ? 1 : 0);
        query += Measure(() =>
        {
            for (var sample = 0; sample < samples; sample++)
            {
                var at = departure + (firstArrival - departure) * sample / samples;
                foreach (var id in ids)
                {
                    var state = movement.StateAt(id, at);
                    checksum += state.PositionKm.X + state.VelocityKmPerSecond.Y;
                }
            }
        });

        // Record validation inputs in the timed loop, then check the arrival details outside it.
        var arrived = new long[ids.Length];
        advance += Measure(() =>
        {
            while (movement.ElapsedTicks < lastArrival)
            {
                var boundaries = movement.Advance(TimeSpan.FromTicks(lastArrival - movement.ElapsedTicks));
                advances++;
                events += boundaries.Length;
                foreach (var boundary in boundaries)
                {
                    if (boundary.Kind != "course-approach-complete") continue;
                    var index = shipIndices[boundary.SpacecraftId];
                    if (arrived[index] != 0) throw new InvalidOperationException("Duplicate arrival.");
                    arrived[index] = boundary.AtTicks;
                    arrivals++;
                }
            }
        });

        for (var i = 0; i < ids.Length; i++)
        {
            if (arrived[i] != plans[i].ArrivalTicks || plans[i].TargetBodyId != target || !plans[i].UsesFiniteBurns
                || movement.CommittedPlan(ids[i]) is not null || movement.PropulsionOf(ids[i])?.Tank.QuantityKg is not > 0)
                throw new InvalidOperationException($"Invalid arrival for {ids[i]} on leg {leg + 1}.");
        }
    }

    if (!double.IsFinite(checksum)) throw new InvalidOperationException("A fleet query returned an invalid state.");
    return new(plan, commit, advance, query, arrivals, events, advances, movement.ElapsedTicks, (long)shipCount * snapshots, checksum);
}

static Measurement Measure(Action action)
{
    var before = GC.GetTotalAllocatedBytes(precise: true);
    var start = Stopwatch.GetTimestamp();
    action();
    return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - before);
}

readonly record struct Measurement(double Milliseconds, long Bytes)
{
    public static Measurement operator +(Measurement left, Measurement right) =>
        new(left.Milliseconds + right.Milliseconds, left.Bytes + right.Bytes);
}

readonly record struct FleetResult(Measurement Plan, Measurement Commit, Measurement Advance, Measurement Query,
    int Arrivals, int Events, int Advances, long FinalTick, long Queries, double Checksum);
