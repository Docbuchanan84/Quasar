namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    internal TrajectorySegment[] TraceCourseCoast(CelestialState initial, string primary, long start, long end,
        CancellationToken cancellationToken, bool validateSurface = true)
    {
        var segments = new List<TrajectorySegment>();
        var coast = new TwoBodyTrajectory(OrbitalNavigation.ToLocal(catalogue, primary, start, initial), start, PrimaryMu(primary));
        var cursor = start;
        while (cursor < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segments.Count >= 32) throw new InvalidOperationException("Course exceeds the gravity transition work limit.");
            var through = cursor + Math.Min(SearchWindow, end - cursor);
            var patch = FindCoastPatch(coast, primary, cursor, through, cancellationToken, buildPatch: true);
            if (patch is not null)
            {
                if (patch.AtTicks > coast.EpochTicks) segments.Add(new(catalogue, primary, coast, patch.AtTicks));
                (cursor, primary, coast) = (patch.AtTicks, patch.Primary, patch.Coast);
            }
            else cursor = through;
        }
        if (coast.EpochTicks < end) segments.Add(new(catalogue, primary, coast, end));
        if (segments.Count == 0) throw new ArgumentException("Course needs a positive coast duration.");
        foreach (var segment in segments.Where(_ => validateSurface))
            if (!CoastSurfaceIsClear(segment, cancellationToken))
                throw new ArgumentException("Course coast violates primary surface clearance.");
        return [.. segments];
    }

    private bool CoastSurfaceIsClear(TrajectorySegment segment, CancellationToken token)
    {
        var surface = Math.Max(1, catalogue.Bodies[segment.PrimaryBodyId].RadiusKm);
        if (segment.Coast.PeriapsisKm > surface) return true;
        var samples = 0;
        return Clear(segment.StartTicks, segment.Coast.StateAt(segment.StartTicks), segment.EndTicks, segment.Coast.StateAt(segment.EndTicks));

        bool Clear(long from, CelestialState first, long through, CelestialState last)
        {
            token.ThrowIfCancellationRequested();
            if (++samples > 8192) throw new InvalidOperationException("Coast surface clearance work limit exceeded.");
            var a = Vector3d.From(first.PositionKm);
            var b = Vector3d.From(last.PositionKm);
            if (a.Length <= surface || b.Length <= surface) return false;
            var seconds = (through - from) / (double)TimeSpan.TicksPerSecond;
            var error = segment.Coast.MaximumAccelerationFrom(first, seconds) * seconds * seconds / 8 + 1e-5;
            if (DistanceToSegment(a, b - a) - error > surface) return true;
            if (through - from <= 1) return false;
            var middle = from + (through - from) / 2;
            var state = segment.Coast.StateAt(middle);
            return Clear(from, first, middle, state) && Clear(middle, state, through, last);
        }
    }
}
