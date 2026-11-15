namespace Quasar.Simulation;

public sealed partial class MovementExecution
{
    private const int DaySeconds = 86400;
    private const int ShortTrimSeconds = 5 * DaySeconds;
    private const int LongTrimSeconds = SailTrajectory.MaximumDurationSeconds;
    private const int MaximumLongRangeTrims = 6;
    private const int LongRangeSailDays = 1825;

    internal Func<CancellationToken, SailCourse[]> CaptureSailCourseSearch(string id, string targetId)
    {
        var ship = spacecraft[id];
        var sail = RequireSail(ship);
        if (ship.Sailing is not null || ship.SailCourse is not null || ship.Coast is not { } coast)
            throw new InvalidOperationException("Feather the sail or cancel the current course before plotting.");
        if (!catalogue.Bodies.TryGetValue(targetId, out var target) || targetId == ship.Primary
            || target.Orbit is null || target.GravitationalParameterKm3PerSecond2 <= 0)
            throw new ArgumentException("Choose another body with a modeled gravity sphere for a flyby.");
        var start = ElapsedTicks;
        var state = new SailCourseState(ship.Primary, start, KinematicState.From(coast.StateAt(start)), targetId, []);
        return token => SearchSailCourses(sail, state, token);
    }

    private SailCourse[] SearchSailCourses(SolarSail sail, SailCourseState departure, CancellationToken token)
    {
        var search = new MovementExecution(catalogue);
        var courses = new List<SailCourse>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            Try([]);
            foreach (var duration in new[] { 21600, 43200, 86400, 129600, 172800, 259200, ShortTrimSeconds })
                foreach (var angle in new[] { 35d, -35d, 0d, 65d, -65d })
                    Try([new(0, duration, angle)]);
            foreach (var first in new[] { 35d, -35d })
                foreach (var second in new[] { 35d, -35d, 0d })
                    Try([new(0, 21600, first), new(43200, 43200, second)]);
            foreach (var wait in new[] { 86400, 30 * 86400, 90 * 86400, 180 * 86400 })
                foreach (var angle in new[] { 35d, -35d })
                {
                    Try([new(wait, 86400, angle)]);
                    Try([new(0, 86400, angle), new(wait, ShortTrimSeconds, angle)]);
                }
            if (courses.Count == 0)
            {
                departure = departure with { LongRange = true };
                Try([]);
                // Let sunlight build up a useful change in orbital velocity over weeks or
                // months, and coast long enough to reach a later encounter window.
                foreach (var waitDays in new[] { 0, 15, 30, 60, 90, 120, 180, 240, 300, 365, 455, 545, 730 })
                    foreach (var trimDays in new[] { 7, 15, 30 })
                        foreach (var angle in new[] { 35d, -35d, 0d, 55d, -55d })
                            Try([new(waitDays * DaySeconds, trimDays * DaySeconds, angle)]);
                foreach (var waitDays in new[] { 0, 30, 90, 180, 300, 455, 730 })
                    foreach (var sections in new[] { 2, 3, 4, MaximumLongRangeTrims })
                        foreach (var angle in new[] { -35d, 35d, 0d })
                            Try([.. Enumerable.Range(0, sections).Select(section =>
                                new SailManeuver(waitDays * DaySeconds + section * LongTrimSeconds, LongTrimSeconds, angle))]);
                foreach (var waitDays in new[] { 30, 60, 90, 120, 180, 240, 365 })
                    foreach (var first in new[] { 35d, -35d, 0d })
                        foreach (var second in new[] { 35d, -35d, 0d })
                            Try([new(0, LongTrimSeconds, first), new(waitDays * DaySeconds, LongTrimSeconds, second)]);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
        return [.. courses];

        void Try(System.Collections.Immutable.ImmutableArray<SailManeuver> maneuvers)
        {
            budget.Token.ThrowIfCancellationRequested();
            try
            {
                if (search.BuildSailCourse(sail, departure with { Maneuvers = maneuvers }, budget.Token) is { } course)
                    courses.Add(course);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
        }
    }

    private SailCourse? BuildSailCourse(SolarSail sail, SailCourseState state, CancellationToken token)
    {
        var maximumTrims = state.LongRange ? MaximumLongRangeTrims : 2;
        var maximumTrimSeconds = state.LongRange ? LongTrimSeconds : ShortTrimSeconds;
        if (state.Maneuvers.IsDefault || state.Maneuvers.Length > maximumTrims || state.InitialRelativeState is null
            || !catalogue.Bodies.TryGetValue(state.TargetId, out var target) || target.Orbit is null
            || target.GravitationalParameterKm3PerSecond2 <= 0 || state.TargetId == state.DeparturePrimary)
            throw new ArgumentException("Invalid sail course.");
        var start = state.DepartureTicks;
        var horizon = checked(start + (state.LongRange ? LongRangeSailDays : 730) * TimeSpan.TicksPerDay);
        var primary = state.DeparturePrimary;
        var cursor = start;
        var coast = new TwoBodyTrajectory(state.InitialRelativeState.ToRuntime(), start, PrimaryMu(primary));
        var legs = new List<SailCourse.Leg>();
        foreach (var maneuver in state.Maneuvers)
        {
            if (maneuver is null || maneuver.DurationSeconds < 1 || maneuver.DurationSeconds > maximumTrimSeconds || maneuver.OffsetSeconds < 0)
                throw new ArgumentException("Invalid sail maneuver.");
            var trimStart = checked(start + (long)maneuver.OffsetSeconds * TimeSpan.TicksPerSecond);
            if (trimStart < cursor || trimStart > horizon - maneuver.DurationSeconds * TimeSpan.TicksPerSecond)
                throw new ArgumentException("Sail maneuvers must be ordered and within the course horizon.");
            if (trimStart > cursor)
            {
                if (TraceTo(trimStart) is { } early)
                    return early with { State = state with { Maneuvers = [.. state.Maneuvers.TakeWhile(entry => entry != maneuver)] } };
            }
            var path = BuildSailPath(primary, sail, new(trimStart, maneuver.DurationSeconds, maneuver.AngleDegrees,
                KinematicState.From(coast.StateAt(trimStart))), token);
            var trimPrimary = primary;
            legs.Add(new(trimStart, path.EndTicks, primary,
                at => OrbitalNavigation.ToSystem(catalogue, trimPrimary, at, path.StateAt(at))));
            cursor = path.EndTicks;
            coast = path.EndCoast;
        }
        return TraceTo(horizon);

        SailCourse? TraceTo(long through)
        {
            var traced = TraceCourseCoast(coast, primary, cursor, through, token, validateSurface: false, stopAtPrimary: target.Id);
            foreach (var segment in traced)
            {
                if (!CoastSurfaceIsClear(segment, token)) throw new ArgumentException("Sail course reaches a surface.");
                if (segment.PrimaryBodyId == target.Id && segment.Coast.ApoapsisKm is null
                    && IsInbound(segment.Coast.StateAt(segment.StartTicks)) && !IsInbound(segment.Coast.StateAt(segment.EndTicks)))
                {
                    var low = segment.StartTicks;
                    var high = segment.EndTicks;
                    while (high - low > 1)
                    {
                        var middle = low + (high - low) / 2;
                        if (IsInbound(segment.Coast.StateAt(middle))) low = middle;
                        else high = middle;
                    }
                    legs.Add(new(segment.StartTicks, high, segment.PrimaryBodyId, segment.StateAt));
                    var local = segment.Coast.StateAt(high);
                    return new(state, sail, [.. legs], high, new(local, high, PrimaryMu(target.Id)),
                        new(target.Id, Vector3d.From(local.PositionKm).Length), Vector3d.From(local.VelocityKmPerSecond).Length);
                }
                legs.Add(new(segment.StartTicks, segment.EndTicks, segment.PrimaryBodyId, segment.StateAt));
            }
            var last = traced[^1];
            (cursor, primary, coast) = (last.EndTicks, last.PrimaryBodyId, last.Coast);
            return null;
        }
    }

    internal SailCourse? CommittedSailCourse(string id) => spacecraft[id].SailCourse;

    internal void CommitSailCourse(string id, SailCourse course)
    {
        var ship = spacecraft[id];
        if (ship.Coast is not { } coast || course.Equipment != RequireSail(ship)
            || course.State.DepartureTicks != ElapsedTicks || course.State.DeparturePrimary != ship.Primary
            || course.State.InitialRelativeState.ToRuntime() != coast.StateAt(ElapsedTicks))
            throw new InvalidOperationException("The sailcraft's departure changed. Plot the course again.");
        ship.SailCourse = course;
        ship.Primary = course.PrimaryAt(ElapsedTicks);
        ship.Coast = null;
        ship.AutomaticPrimary = false;
        ship.NextBoundary = 0;
        ship.Generation++;
        Schedule(id, ship);
        CompactQueue();
    }
}
