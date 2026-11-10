namespace Quasar.Simulation;

public sealed partial class TrajectoryPlanner
{
    internal TrajectoryPlan? RefineFiniteCourse(CelestialState initial, long departureTicks, long arrivalTicks,
        string primaryId, string targetId, string arrivalKind, CelestialState target, SpacecraftPropulsion propulsion,
        double? budget, bool longWay, CancellationToken cancellationToken, out string reason)
    {
        const int maximumIterations = 16;
        var primary = catalogue.Bodies[primaryId];
        var clearance = Math.Max(1, primary.RadiusKm);
        var mu = primary.GravitationalParameterKm3PerSecond2;
        var performance = propulsion.CalculatePerformance();
        var exhaustSpeed = propulsion.Engine.ExhaustVelocityMPerSecond / 1000;
        var local = OrbitalNavigation.ToLocal(catalogue, primaryId, departureTicks, initial);
        var arrivalLocal = OrbitalNavigation.ToLocal(catalogue, primaryId, arrivalTicks, target);
        var initialVelocity = Vector3d.From(local.VelocityKmPerSecond);
        var targetVelocity = Vector3d.From(arrivalLocal.VelocityKmPerSecond);
        reason = "No supported Lambert arc reached the requested arrival state.";
        try
        {
            var seed = Plan(new(initial, departureTicks,
                new(primaryId, arrivalTicks, new(null, target, arrivalTicks), longWay, clearance)));
            var departure = Vector3d.From(seed.Burns[0].DeltaVelocityKmPerSecond);
            var braking = Vector3d.From(seed.Burns[^1].DeltaVelocityKmPerSecond);

            for (var i = 0; i < maximumIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reason = "Insufficient propellant or maneuver budget for departure and braking.";
                var total = departure.Length + braking.Length;
                if (!double.IsFinite(total) || total >= performance.TheoreticalDeltaVMPerSecond / 1000 || total > budget)
                    return null;

                var firstTicks = BurnTicks(performance.WetMassKg, departure.Length);
                reason = "The installed engine cannot complete a required burn within the supported duration.";
                if (firstTicks is < 1 or > PoweredTrajectory.MaximumCourseDurationSeconds * TimeSpan.TicksPerSecond) return null;
                var first = new PoweredTrajectory(local, (departure / departure.Length).Velocity, mu, propulsion, departureTicks, firstTicks);
                var remaining = first.RemainingPropellantKg(first.EndTicks);
                var lastTicks = BurnTicks(performance.DryMassKg + remaining, braking.Length);
                if (lastTicks is < 1 or > PoweredTrajectory.MaximumCourseDurationSeconds * TimeSpan.TicksPerSecond) return null;
                var lastStart = checked(arrivalTicks - lastTicks);
                reason = "The transfer leaves too little time or propellant for both burns and the coast.";
                if (first.ExhaustsPropellant || lastStart <= first.EndTicks) return null;

                var lastPropulsion = FiniteCourse.WithPropellant(propulsion, remaining);
                var lastDirection = (braking / braking.Length).Velocity;
                var reverse = new PoweredTrajectory(arrivalLocal, lastDirection, mu, lastPropulsion, lastStart, lastTicks);
                if (reverse.ExhaustsPropellant) return null;

                reason = "Finite-burn refinement did not converge on the requested arrival state.";
                var from = first.StateAt(first.EndTicks);
                var to = reverse.InitialStateFor(arrivalLocal);
                var coastSeconds = (lastStart - first.EndTicks) / (double)TimeSpan.TicksPerSecond;
                var coastVelocity = LambertSolver.DepartureVelocity(from.PositionKm, to.PositionKm, coastSeconds, mu, longWay);
                var referenceCoast = new TwoBodyTrajectory(new(from.PositionKm, coastVelocity), first.EndTicks, mu);
                var referenceEnd = referenceCoast.StateAt(lastStart);
                var departureCorrection = Vector3d.From(coastVelocity) - Vector3d.From(from.VelocityKmPerSecond);
                var brakingCorrection = Vector3d.From(to.VelocityKmPerSecond) - Vector3d.From(referenceEnd.VelocityKmPerSecond);

                if (departureCorrection.Length * coastSeconds <= FiniteCourse.PositionToleranceKm * 4
                    && brakingCorrection.Length <= FiniteCourse.VelocityToleranceKmPerSecond)
                {
                    FiniteCourse? finite = null;
                    try
                    {
                        finite = FiniteCourse.Build(catalogue, new(primaryId, targetId, arrivalKind, departureTicks, arrivalTicks,
                            KinematicState.From(initial), KinematicState.From(arrivalLocal), propulsion,
                            CartesianState.From((departure / departure.Length).Velocity), firstTicks, CartesianState.From(lastDirection), lastTicks));
                    }
                    catch (ArgumentException)
                    {
                        // Tick-rounded burn durations can straddle a valid endpoint. Keep refining,
                        // while the full forward check retains the actual arrival tolerances.
                    }
                    if (finite is not null)
                    {
                        var result = finite.ToPlan();
                        if (result.RequiredDeltaVKmPerSecond > budget)
                        {
                            reason = "The refined burns exceed the requested maneuver budget.";
                            return null;
                        }
                        reason = "The route crosses a surface or gravity boundary, or clearance could not be verified within the search limit.";
                        if (new MovementExecution(catalogue).CourseCrossesPrimaryBoundary(finite, cancellationToken))
                            return null;
                        return result;
                    }
                }

                departure += departureCorrection;
                braking += brakingCorrection;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            // Invalid geometry and numerical failures reject only this candidate.
        }
        return null;

        long BurnTicks(double wetMass, double deltaV)
        {
            var seconds = -wetMass * double.ExpM1(-deltaV / exhaustSpeed) / performance.RatedMassFlowKgPerSecond;
            return seconds <= PoweredTrajectory.MaximumCourseDurationSeconds
                ? Math.Max(1, (long)Math.Round(seconds * TimeSpan.TicksPerSecond))
                : long.MaxValue;
        }
    }
}
