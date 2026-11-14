using System.Text.Json.Serialization;

namespace Quasar.Simulation;

public sealed record FlightForecast(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long StartTicks,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long EndTicks,
    TrajectoryDisplayPoint[] Points, CourseGravityTransition[] GravityTransitions);

public sealed partial class MovementExecution
{
    private const long ForecastHorizon = 180 * TimeSpan.TicksPerDay;

    internal string FlightPathVersion(string id)
    {
        if (!spacecraft.TryGetValue(id, out var ship)) throw new ArgumentException("Choose a modeled spacecraft.", nameof(id));
        return FormattableString.Invariant($"{ship.Generation}:{(ship.Plan is null ? 0 : 1)}");
    }

    // Capture on the writer. The forecast uses immutable paths and the catalogue after releasing it.
    internal Func<CancellationToken, FlightForecast> CaptureFlightForecast(string id)
    {
        if (!spacecraft.TryGetValue(id, out var ship)) throw new ArgumentException("Choose a modeled spacecraft.", nameof(id));
        var start = ElapsedTicks;
        var primary = ship.Primary;
        var plan = ship.Plan;
        var sail = ship.Sailing;
        var burn = ship.Burn;
        var rendezvous = ship.Rendezvous;
        var automatic = ship.AutomaticPrimary || sail is not null || burn is not null;
        var coast = sail?.EndCoast ?? burn?.EndCoast ?? ship.Coast;
        var initial = StateAt(id, start);
        return Forecast;

        FlightForecast Forecast(CancellationToken token)
        {
            var end = start > long.MaxValue - ForecastHorizon ? long.MaxValue : start + ForecastHorizon;
            var points = new List<TrajectoryDisplayPoint>();
            var transitions = new List<CourseGravityTransition>();
            AddPoint(start, initial);
            if (plan is not null)
            {
                end = Math.Min(end, plan.ArrivalTicks);
                var boundaries = plan.Finite?.Boundaries.Select(entry => entry.Ticks)
                    ?? plan.Segments.Select(segment => segment.EndTicks);
                AddLegs(start, end, boundaries, plan.StateAt);
                if (plan.Finite is { } finite)
                    foreach (var at in finite.GravityTransitionTicks.Where(at => at > start && at <= end))
                        AddTransition(at, finite.PrimaryAt(at));
                else
                    foreach (var segment in plan.Segments.Where(segment => segment.StartTicks > start && segment.StartTicks <= end))
                        AddTransition(segment.StartTicks, segment.PrimaryBodyId);
            }
            else if (rendezvous is { } target)
            {
                AddSamples(start, end, at => target.Destination.StateAt(catalogue, at));
            }
            else
            {
                var coastStart = start;
                if (sail is not null)
                {
                    coastStart = sail.EndTicks;
                    AddSamples(start, Math.Min(end, coastStart),
                        at => OrbitalNavigation.ToSystem(catalogue, primary, at, sail.StateAt(at)));
                }
                else if (burn is not null)
                {
                    coastStart = burn.Path.EndTicks;
                    AddSamples(start, Math.Min(end, coastStart),
                        at => OrbitalNavigation.ToSystem(catalogue, primary, at, burn.Path.StateAt(at)));
                }
                if (coastStart < end)
                {
                    var finalCoast = coast!;
                    if (finalCoast.PeriodSeconds is > 0 and < 120 * 86400
                        && (!automatic || catalogue.GetEnvironment(primary).SphereOfInfluenceRadiusKm is { } sphere
                            && finalCoast.ApoapsisKm < sphere * .99))
                    {
                        // A short bound orbit needs enough points per revolution to stay readable.
                        var orbitTicks = TimeSpan.FromSeconds(Math.Max(1, finalCoast.PeriodSeconds.Value * 1.25)).Ticks;
                        end = coastStart + Math.Min(end - coastStart, orbitTicks);
                    }
                    if (automatic) AddPatchedCoast(coastStart, end, finalCoast, primary);
                    else AddSamples(coastStart, end, at => OrbitalNavigation.ToSystem(catalogue, primary, at, finalCoast.StateAt(at)));
                }
            }
            return new(start, end, [.. points], [.. transitions]);

            void AddPatchedCoast(long from, long through, TwoBodyTrajectory current, string currentPrimary)
            {
                var cursor = from;
                var segmentStart = from;
                while (cursor < through)
                {
                    token.ThrowIfCancellationRequested();
                    if (transitions.Count >= 32) throw new InvalidOperationException("Flight forecast exceeds the gravity transition work limit.");
                    var limit = cursor + Math.Min(through - cursor, SearchWindow);
                    var patch = FindCoastPatch(current, currentPrimary, cursor, limit, token,
                        buildPatch: true, skipUnsupportedHandoff: true);
                    if (patch is null)
                    {
                        cursor = limit;
                        continue;
                    }
                    AddCoast(segmentStart, patch.AtTicks);
                    AddTransition(patch.AtTicks, patch.Primary);
                    (cursor, segmentStart, currentPrimary, current) = (patch.AtTicks, patch.AtTicks, patch.Primary, patch.Coast);
                }
                AddCoast(segmentStart, through);

                void AddCoast(long first, long last)
                {
                    CelestialState Sample(long at) => OrbitalNavigation.ToSystem(catalogue, currentPrimary, at, current.StateAt(at));
                    if (last > first && current.ApoapsisKm is null && IsInbound(current.StateAt(first))
                        && !IsInbound(current.StateAt(last)))
                    {
                        var low = first;
                        var high = last;
                        while (high - low > 1)
                        {
                            var middle = low + (high - low) / 2;
                            if (IsInbound(current.StateAt(middle))) low = middle;
                            else high = middle;
                        }
                        AddSamples(first, high, Sample, clusterEnds: true);
                        AddSamples(high, last, Sample, clusterEnds: true);
                    }
                    else AddSamples(first, last, Sample);
                }
            }

            void AddLegs(long from, long through, IEnumerable<long> boundaries, Func<long, CelestialState> sample)
            {
                foreach (var boundary in boundaries.Where(at => at > from && at < through).Distinct().Order())
                {
                    AddSamples(from, boundary, sample);
                    from = boundary;
                }
                AddSamples(from, through, sample);
            }

            void AddSamples(long from, long through, Func<long, CelestialState> sample, bool clusterEnds = false)
            {
                if (through <= from) return;
                // Give short powered legs and close encounters their own samples.
                var count = Math.Clamp((int)((through - from) / TimeSpan.TicksPerDay), 16, 160);
                for (var index = 1; index <= count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var at = clusterEnds && index < count
                        ? from + (long)((through - from) * (1 - Math.Cos(Math.PI * index / count)) / 2)
                        : from + (through - from) * index / count;
                    AddPoint(at, sample(at));
                }
            }

            void AddPoint(long at, CelestialState state)
            {
                var tick = Ticks(at);
                if (points.Count == 0 || points[^1].AtTicks != tick)
                    points.Add(new(tick, state.PositionKm, state.VelocityKmPerSecond));
            }

            void AddTransition(long at, string bodyId) =>
                transitions.Add(new(bodyId, catalogue.Bodies[bodyId].Name, Ticks(at)));
        }
    }

    private static string Ticks(long at) => at.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
