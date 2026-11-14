using System.Collections.Immutable;

namespace Quasar.Simulation;

public sealed record SailManeuver(int OffsetSeconds, int DurationSeconds, double AngleDegrees);
public sealed record SailCourseState(string DeparturePrimary, long DepartureTicks, KinematicState InitialRelativeState,
    string TargetId, ImmutableArray<SailManeuver> Maneuvers);
public sealed record CourseSailLeg(string StartTicks, double DurationSeconds, double AngleDegrees);

internal sealed record SailCourse(SailCourseState State, SolarSail Equipment, SailCourse.Leg[] Legs,
    long ArrivalTicks, TwoBodyTrajectory ArrivalCoast, FlybyEncounter Flyby, double RelativeSpeedKmPerSecond)
{
    internal sealed record Leg(long Start, long End, string Primary, Func<long, CelestialState> Sample);
    internal CelestialState StateAt(long at) => LegAt(at).Sample(at);
    internal string PrimaryAt(long at) => LegAt(at).Primary;
    private Leg LegAt(long at) => Legs.Last(leg => leg.Start <= at);
    internal string[] AssistBodies => Legs.Zip(Legs.Skip(1), (before, after) => (before.Primary, Next: after.Primary))
        .Where(change => change.Primary != change.Next && change.Next != SolarSystem.Sun && change.Next != State.TargetId)
        .Select(change => change.Next).ToArray();
    internal double DeployedSeconds => State.Maneuvers.Sum(maneuver => (double)maneuver.DurationSeconds);
    internal (long At, string Kind)[] Boundaries { get; } = Legs.Skip(1).Select(leg => (leg.Start, "sail-course-leg"))
        .Append((ArrivalTicks, "sail-flyby")).ToArray();
    internal TrajectoryDisplayPoint[] DisplayPoints() => Legs.SelectMany((leg, legIndex) =>
        Enumerable.Range(legIndex == 0 ? 0 : 1, legIndex == 0 ? 33 : 32).Select(i =>
        {
            var at = leg.Start + (leg.End - leg.Start) * i / 32;
            var state = leg.Sample(at);
            return new TrajectoryDisplayPoint(Ticks(at), state.PositionKm, state.VelocityKmPerSecond);
        })).ToArray();

    internal static string Ticks(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
