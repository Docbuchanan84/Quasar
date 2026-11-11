namespace Quasar.Simulation;

public sealed record CourseChoices(TrajectoryPlan Fastest, TrajectoryPlan LowestManeuverCost, TrajectoryPlan? Tuned = null);

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

    internal static CourseChoices? Find(Func<int, TrajectoryPlan?> evaluate, CancellationToken cancellationToken,
        int? preferredSeconds = null, bool lockFlightTime = false, Func<bool>? stop = null, int[]? probes = null)
    {
        var candidates = new Dictionary<int, TrajectoryPlan?>();
        if (preferredSeconds is { } preferred && lockFlightTime)
        {
            Evaluate(preferred);
            return candidates.GetValueOrDefault(preferred) is { } exact ? new(exact, exact, exact) : null;
        }
        foreach (var duration in probes ?? []) Evaluate(duration);
        if (preferredSeconds is { } requestedTime)
        {
            Evaluate(requestedTime);
            Evaluate(Math.Max(1, requestedTime - requestedTime / 5));
            Evaluate(Math.Min(MaximumSeconds, requestedTime + requestedTime / 5));
        }
        foreach (var duration in Durations) Evaluate(duration);
        var feasible = candidates.Where(pair => pair.Value is not null).Select(pair => pair.Key).Order().ToArray();
        if (feasible.Length == 0) return null;

        if (preferredSeconds is { } target && candidates.GetValueOrDefault(target) is null)
        {
            var valid = feasible.MinBy(seconds => Math.Abs((long)seconds - target));
            var invalid = target;
            for (var attempt = 0; attempt < 6 && Math.Abs((long)valid - invalid) > 1; attempt++)
            {
                var middle = invalid + (valid - invalid) / 2;
                if (Evaluate(middle) is null) invalid = middle;
                else valid = middle;
            }
        }

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
            ranked.OrderBy(plan => plan.RequiredDeltaVKmPerSecond).ThenBy(plan => plan.ArrivalTicks).First(),
            preferredSeconds is { } requested ? candidates.Where(pair => pair.Value is not null)
                .OrderBy(pair => Math.Abs((long)pair.Key - requested)).ThenBy(pair => pair.Value!.RequiredDeltaVKmPerSecond)
                .First().Value : null);

        TrajectoryPlan? Evaluate(int duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stop?.Invoke() == true) return null;
            if (!candidates.TryGetValue(duration, out var plan))
            {
                try { plan = evaluate(duration); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && stop?.Invoke() == true) { return null; }
                candidates.Add(duration, plan);
            }
            return plan;
        }
    }
}
