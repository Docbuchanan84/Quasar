namespace Quasar.Simulation;

public sealed record CourseChoices(TrajectoryPlan Fastest, TrajectoryPlan LowestManeuverCost);

internal static class CourseSearch
{
    internal const int MaximumSeconds = 315576000;
    private const int SampleCount = 40;
    private static readonly int[] Durations = CreateDurations();

    private static int[] CreateDurations()
    {
        var durations = new int[SampleCount];
        durations[0] = 1;
        for (var index = 1; index < durations.Length; index++)
        {
            var seconds = (int)Math.Round(Math.Pow(MaximumSeconds, (double)index / (SampleCount - 1)));
            // Whole-second rounding can give adjacent early samples the same duration.
            durations[index] = Math.Max(durations[index - 1] + 1, seconds);
        }
        return durations;
    }

    internal static CourseChoices? Find(Func<int, TrajectoryPlan?> evaluate, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<int, TrajectoryPlan?>();
        foreach (var duration in Durations) Evaluate(duration);
        var feasible = Durations.Where(duration => candidates[duration] is not null).ToArray();
        if (feasible.Length == 0) return null;

        var fastestIndex = Array.BinarySearch(Durations, feasible[0]);
        if (fastestIndex > 0)
        {
            var low = Durations[fastestIndex - 1];
            var high = Durations[fastestIndex];
            for (var attempt = 0; attempt < 8 && high - low > 1; attempt++)
            {
                var middle = low + (high - low) / 2;
                if (Evaluate(middle) is null) low = middle;
                else high = middle;
            }
        }

        var cheapest = candidates.Where(pair => pair.Value is not null)
            .OrderBy(pair => pair.Value!.RequiredDeltaVKmPerSecond).ThenBy(pair => pair.Key).First().Key;
        var lowerNeighbor = Durations.LastOrDefault(duration => duration < cheapest, cheapest);
        var upperNeighbor = Durations.FirstOrDefault(duration => duration > cheapest, cheapest);
        for (var sample = 1; sample <= 4; sample++)
        {
            Evaluate(cheapest - (int)((long)(cheapest - lowerNeighbor) * sample / 5));
            Evaluate(cheapest + (int)((long)(upperNeighbor - cheapest) * sample / 5));
        }

        var ranked = candidates.Values.OfType<TrajectoryPlan>();
        return new(
            ranked.OrderBy(plan => plan.ArrivalTicks).ThenBy(plan => plan.RequiredDeltaVKmPerSecond).First(),
            ranked.OrderBy(plan => plan.RequiredDeltaVKmPerSecond).ThenBy(plan => plan.ArrivalTicks).First());

        TrajectoryPlan? Evaluate(int duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidates.TryGetValue(duration, out var plan))
                candidates.Add(duration, plan = evaluate(duration));
            return plan;
        }
    }
}
