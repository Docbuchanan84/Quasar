namespace Quasar.Simulation;

public sealed partial class SystemLab
{
    private sealed record SolarWindow(long BurnStart, long Arrival, CelestialState BurnOrigin, CelestialState Target,
        bool LongWay, double SeedDeltaV);

    private CourseChoices? FindLaterSolarCourse(CelestialState initial, long departure, string destinationId,
        double approachRadius, SpacecraftPropulsion propulsion, double? maneuverBudget, int estimateSeconds,
        int? preferredSeconds, CancellationToken requestToken, CancellationToken searchToken)
    {
        const string primary = SolarSystem.Sun;
        const long waitStep = 15 * TimeSpan.TicksPerDay;
        const long maximumWait = 3 * 365 * TimeSpan.TicksPerDay;
        var primaryBody = celestialPositions.Bodies[primary];
        var mu = primaryBody.GravitationalParameterKm3PerSecond2;
        var available = propulsion.CalculatePerformance().TheoreticalDeltaVMPerSecond / 1000;
        var budget = Math.Min(available, maneuverBudget ?? available);
        var candidates = new List<SolarWindow>();
        var verified = new List<TrajectoryPlan>();
        try
        {
            // Rank cheap impulsive seeds across multiple departure geometries. Each returned
            // course is rebuilt with a safe wait and finite burns; seeds are never executable.
            var coasts = new MovementExecution(celestialPositions).TraceCourseCoast(initial, primary,
                departure, checked(departure + maximumWait), searchToken, validateSurface: false);
            for (var wait = waitStep; wait <= maximumWait; wait += waitStep)
            {
                searchToken.ThrowIfCancellationRequested();
                var ignition = checked(departure + wait);
                var coast = coasts.Last(segment => segment.StartTicks <= ignition);
                if (coast.PrimaryBodyId != primary) continue;
                var origin = coast.StateAt(ignition);
                var local = OrbitalNavigation.ToLocal(celestialPositions, primary, ignition, origin);
                foreach (var step in Enumerable.Range(8, 25))
                {
                    var seconds = estimateSeconds * (long)step / 16;
                    if (seconds < 1 || seconds + wait / TimeSpan.TicksPerSecond > CourseSearch.MaximumSeconds) continue;
                    var arrival = checked(ignition + seconds * TimeSpan.TicksPerSecond);
                    var target = ApproachState(destinationId, primary, approachRadius, arrival, origin.PositionKm);
                    var end = OrbitalNavigation.ToLocal(celestialPositions, primary, arrival, target);
                    foreach (var longWay in new[] { false, true })
                    {
                        if (EstimateCost(local, end, ignition, arrival, longWay) is { } cost && cost < budget)
                            candidates.Add(new(ignition, arrival, origin, target, longWay, cost));
                    }
                }
            }

            var selected = candidates.OrderBy(window => window.Arrival).Take(16)
                .Concat(candidates.OrderBy(window => window.SeedDeltaV).Take(16));
            if (preferredSeconds is { } preferred)
            {
                var preferredArrival = checked(departure + preferred * (long)TimeSpan.TicksPerSecond);
                selected = selected.Concat(candidates.OrderBy(window => Math.Abs(window.Arrival - preferredArrival)).Take(8));
            }
            foreach (var window in selected.Distinct())
            {
                searchToken.ThrowIfCancellationRequested();
                try
                {
                    var plan = planner.RefineFiniteCourse(window.BurnOrigin, window.BurnStart, window.Arrival, primary,
                        destinationId, "approach", window.Target, propulsion, maneuverBudget, window.LongWay, searchToken, out _);
                    if (plan?.Finite is not { } course) continue;
                    var scheduled = FiniteCourse.Build(celestialPositions, course.State with
                    {
                        DepartureTicks = departure,
                        InitialState = KinematicState.From(initial),
                        CoastBeforeBurnTicks = window.BurnStart - departure
                    }, searchToken);
                    verified.Add(scheduled.ToPlan());
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
                {
                    // A rejected candidate can leave other departure windows open.
                }
            }
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            // No usable coast to a later ignition epoch from this departure state.
        }
        requestToken.ThrowIfCancellationRequested();
        if (verified.Count == 0) return null;
        return new(verified.MinBy(plan => (plan.ArrivalTicks, plan.RequiredDeltaVKmPerSecond))!,
            verified.MinBy(plan => (plan.RequiredDeltaVKmPerSecond, plan.ArrivalTicks))!,
            preferredSeconds is { } goal ? verified.MinBy(plan => Math.Abs(plan.TravelSeconds - goal)) : null,
            verified.MinBy(CourseSearch.SampledDistance));

        double? EstimateCost(CelestialState local, CelestialState end, long ignition, long arrival, bool longWay)
        {
            try
            {
                var seconds = (arrival - ignition) / (double)TimeSpan.TicksPerSecond;
                var velocity = LambertSolver.DepartureVelocity(local.PositionKm, end.PositionKm, seconds, mu, longWay);
                var transfer = new TwoBodyTrajectory(new(local.PositionKm, velocity), ignition, mu);
                if (transfer.PeriapsisKm <= primaryBody.RadiusKm) return null;
                return (Vector3d.From(velocity) - Vector3d.From(local.VelocityKmPerSecond)).Length
                    + (Vector3d.From(transfer.StateAt(arrival).VelocityKmPerSecond) - Vector3d.From(end.VelocityKmPerSecond)).Length;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
            {
                return null;
            }
        }
    }
}
