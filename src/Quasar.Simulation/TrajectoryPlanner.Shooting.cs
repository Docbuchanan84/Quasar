namespace Quasar.Simulation;

public sealed partial class TrajectoryPlanner
{
    private TrajectoryPlan? ShootPatchedCourse(CelestialState initial, long start, long end, string primary,
        string arrivalPrimary, string targetId, string kind, CelestialState target, SpacecraftPropulsion propulsion,
        Vector3d departure, Vector3d braking, CancellationToken token)
    {
        var performance = propulsion.CalculatePerformance();
        var exhaust = propulsion.Engine.ExhaustVelocityMPerSecond / 1000;
        var tracer = new MovementExecution(catalogue);
        var scale = (end - start) / (double)TimeSpan.TicksPerSecond;
        var input = new[] { departure.X, departure.Y, departure.Z, braking.X, braking.Y, braking.Z };
        for (var iteration = 0; iteration < 20; iteration++)
        {
            token.ThrowIfCancellationRequested();
            var current = Forward(input);
            if (current is null) return null;
            var error = current.Value.Error;
            if (Norm(error.Take(3)) * scale < FiniteCourse.PositionToleranceKm && Norm(error.Skip(3)) < FiniteCourse.VelocityToleranceKmPerSecond)
            {
                try { return FiniteCourse.Build(catalogue, current.Value.State, token).ToPlan(); }
                catch (ArgumentException) { return null; }
            }
            var matrix = new double[6, 7];
            for (var axis = 0; axis < 6; axis++)
            {
                const double step = 1e-5;
                var shifted = (double[])input.Clone();
                shifted[axis] += step;
                var sample = Forward(shifted);
                if (sample is null) return null;
                for (var row = 0; row < 6; row++) matrix[row, axis] = (sample.Value.Error[row] - error[row]) / step;
            }
            for (var row = 0; row < 6; row++) matrix[row, 6] = -error[row];
            for (var column = 0; column < 6; column++)
            {
                var pivot = column;
                for (var row = column + 1; row < 6; row++)
                    if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
                if (Math.Abs(matrix[pivot, column]) < 1e-12) return null;
                for (var j = column; j <= 6; j++) (matrix[pivot, j], matrix[column, j]) = (matrix[column, j], matrix[pivot, j]);
                var divisor = matrix[column, column];
                for (var j = column; j <= 6; j++) matrix[column, j] /= divisor;
                for (var row = 0; row < 6; row++)
                {
                    if (row == column) continue;
                    var factor = matrix[row, column];
                    for (var j = column; j <= 6; j++) matrix[row, j] -= factor * matrix[column, j];
                }
            }
            var accepted = false;
            for (var fraction = 1d; fraction >= 1d / 32; fraction /= 2)
            {
                var adjusted = input.Select((value, axis) => value + fraction * matrix[axis, 6]).ToArray();
                var next = Forward(adjusted);
                if (next is not null && Norm(next.Value.Error) < Norm(error))
                {
                    input = adjusted;
                    accepted = true;
                    break;
                }
            }
            if (!accepted) return null;
        }
        return null;

        (FiniteCourseState State, double[] Error)? Forward(double[] values)
        {
            token.ThrowIfCancellationRequested();
            var first = new Vector3d(values[0], values[1], values[2]);
            var last = new Vector3d(values[3], values[4], values[5]);
            if (first.Length + last.Length >= performance.TheoreticalDeltaVMPerSecond / 1000) return null;
            var (firstTicks, firstThrottle) = Timing(performance.WetMassKg, first.Length);
            var remaining = performance.AvailablePropellantKg - firstTicks / (double)TimeSpan.TicksPerSecond * firstThrottle * performance.RatedMassFlowKgPerSecond;
            var (lastTicks, lastThrottle) = Timing(performance.DryMassKg + remaining, last.Length);
            if (firstTicks <= 0 || lastTicks <= 0 || firstTicks > TimeSpan.TicksPerDay || lastTicks > TimeSpan.TicksPerDay || start + firstTicks >= end - lastTicks) return null;
            try
            {
                var launch = CourseBurn.Build(catalogue, initial, primary, start, firstTicks, (first / first.Length).Velocity,
                    FiniteCourse.Throttled(propulsion, firstThrottle), token, validateSurface: false);
                var coast = tracer.TraceCourseCoast(launch.StateAt(launch.EndTicks), launch.PrimaryAt(launch.EndTicks),
                    launch.EndTicks, end - lastTicks, token, validateSurface: false)[^1];
                var stop = CourseBurn.Build(catalogue, coast.StateAt(coast.EndTicks), coast.PrimaryBodyId, coast.EndTicks,
                    lastTicks, (last / last.Length).Velocity, FiniteCourse.Throttled(FiniteCourse.WithPropellant(propulsion, remaining), lastThrottle), token, validateSurface: false);
                var achieved = stop.StateAt(end);
                var position = (Vector3d.From(achieved.PositionKm) - Vector3d.From(target.PositionKm)) / scale;
                var velocity = Vector3d.From(achieved.VelocityKmPerSecond) - Vector3d.From(target.VelocityKmPerSecond);
                return (new(primary, targetId, kind, start, end, KinematicState.From(initial),
                    KinematicState.From(OrbitalNavigation.ToLocal(catalogue, arrivalPrimary, end, target)), propulsion,
                    CartesianState.From((first / first.Length).Velocity), firstTicks, CartesianState.From((last / last.Length).Velocity),
                    lastTicks, arrivalPrimary, firstThrottle, lastThrottle), [position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z]);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException) { return null; }
        }

        (long Ticks, double Throttle) Timing(double mass, double deltaV)
        {
            var seconds = -mass * double.ExpM1(-deltaV / exhaust) / performance.RatedMassFlowKgPerSecond;
            if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 86400) return (long.MaxValue / 4, 1);
            var ticks = Math.Max(1, (long)Math.Ceiling(seconds * TimeSpan.TicksPerSecond));
            return (ticks, Math.Min(1, seconds / (ticks / (double)TimeSpan.TicksPerSecond)));
        }
        static double Norm(IEnumerable<double> numbers) => Math.Sqrt(numbers.Sum(number => number * number));
    }
}
