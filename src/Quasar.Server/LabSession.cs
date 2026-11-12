using Quasar.Simulation;
using Quasar.Server.Notifications;

namespace Quasar.Server;

public sealed record LabCommand(string InstanceId, long Revision, string ClientId, string Operation,
    int? DurationSeconds = null, string? Mode = null, string? DestinationId = null, int? FlightSeconds = null,
    string? PreviewId = null, string? BurnDirection = null, int? BurnSeconds = null, string? ShipId = null,
    FusionTorchDesign? TorchDesign = null);
public sealed record CoursePreview(string? PreviewId, string InstanceId, long Revision, string ShipId,
    string DestinationId, double SampledDistanceKm, string[] Warnings, TrajectoryDisplay Trajectory);
public sealed record AutomaticCoursePreview(CoursePreview? Fastest, CoursePreview? LowestManeuverCost, string[] InfeasibleReasons,
    CoursePreview? Tuned = null);
public sealed record SessionSnapshot(string InstanceId, long Revision, bool Running, string StopReason,
    int DurationSeconds, string Mode, LabSnapshot World, LabEvent? Interrupt, LabEvent? FlightNotice = null);

/// <summary>The host is the only writer. This gate also makes polling snapshots internally consistent.</summary>
public sealed partial class LabSession(bool startWithWorld = true)
{
    private readonly object gate = new();
    private SystemLab? world = startWithWorld ? new() : null;
    internal LabSession(SystemLab fixture) : this(false) => world = fixture;

    private readonly Dictionary<string, (long Revision, TrajectoryPlan Plan)> previews = [];
    private string instanceId = Guid.NewGuid().ToString();
    private long revision;
    private bool running;
    private string stopReason = "Ready";
    private int durationSeconds = 3600;
    private string mode = "once";
    private LabEvent? interrupt;
    private LabEvent? flightNotice;
    public static readonly int[] Durations = [1, 5, 30, 60, 3600, 86400, 432000, 1296000, 2592000];

    public SessionSnapshot Snapshot()
    {
        lock (gate) return Read();
    }

    // Capture the catalogue under the gate, then query it independently of campaign replacement.
    public CelestialState CelestialStateAt(string bodyId, long atTicks)
    {
        ICelestialPositionService catalogue;
        lock (gate) catalogue = RequireWorld().CelestialPositions;
        return catalogue.GetState(bodyId, atTicks);
    }

    public TrajectoryDisplay PreviewTravel(string destinationId, int flightSeconds)
    {
        lock (gate) return SystemLab.Present(RequireWorld().PreviewTravel(destinationId, flightSeconds));
    }

    public OrbitalTargetState TargetStateAt(string kind, string targetId, long atTicks)
    {
        lock (gate) return RequireWorld().TargetStateAt(kind, targetId, atTicks);
    }

    public CelestialState ShipStateAt(long atTicks)
    {
        lock (gate) return RequireWorld().ShipStateAt(atTicks);
    }

    public CoursePreview PreviewCourse(string destinationId, int flightSeconds, double? maneuverBudget = null)
    {
        lock (gate)
        {
            if (running) throw new InvalidOperationException("Stop time processing before plotting a course.");
            var activeWorld = RequireWorld();
            var plan = activeWorld.PreviewTravel(destinationId, flightSeconds, maneuverBudget);
            return PresentPreview(destinationId, plan, executable: !activeWorld.HasModeledPropulsion);
        }
    }

    public AutomaticCoursePreview PreviewAutomaticCourse(string destinationId, double? maneuverBudget,
        CancellationToken cancellationToken, string arrivalKind = "approach", int? preferredFlightSeconds = null,
        double? reservePercent = null, bool lockFlightTime = false)
    {
        string departureInstance;
        long departureRevision;
        Func<CancellationToken, CourseSearchResult> search;
        lock (gate)
        {
            if (running) throw new InvalidOperationException("Stop time processing before plotting a course.");
            search = RequireWorld().CaptureCourseSearch(destinationId, maneuverBudget, arrivalKind,
                preferredFlightSeconds, reservePercent, lockFlightTime);
            departureInstance = instanceId;
            departureRevision = revision;
        }

        var result = search(cancellationToken);

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (running || switchingCampaign || shuttingDown || departureInstance != instanceId || departureRevision != revision)
                throw new InvalidOperationException("State changed during planning. Refresh state and recalculate.");
            if (result.Choices is not { } choices) return new(null, null, result.InfeasibleReasons);

            var fastest = PresentPreview(destinationId, choices.Fastest);
            var lowestCost = ReferenceEquals(choices.Fastest, choices.LowestManeuverCost)
                ? null
                : PresentPreview(destinationId, choices.LowestManeuverCost);
            var tuned = choices.Tuned is not { } selected ? null
                : ReferenceEquals(selected, choices.Fastest) ? fastest
                : ReferenceEquals(selected, choices.LowestManeuverCost) ? lowestCost ?? fastest
                : PresentPreview(destinationId, selected);
            return new(fastest, lowestCost, [], tuned);
        }
    }

    private CoursePreview PresentPreview(string destinationId, TrajectoryPlan plan, bool executable = true)
    {
        var trajectory = SystemLab.Present(plan);
        var distance = trajectory.Points.Zip(trajectory.Points.Skip(1),
            (a, b) => a.PositionKm.DistanceTo(b.PositionKm)).Sum();
        // Bounded, disposable tokens retain solved plans, never client-provided geometry.
        string? id = null;
        if (executable)
        {
            if (previews.Count >= 32) previews.Remove(previews.Keys.First());
            id = Guid.NewGuid().ToString();
            previews.Add(id, (revision, plan));
        }
        return new(id, instanceId, revision, SystemLab.ShipId, destinationId, distance, trajectory.Arrival is null
            ? [executable ? "Legacy test course with instantaneous maneuvers and no propulsion accounting."
                : "Advisory only. Use automatic course planning to issue an order for this spacecraft."]
            : ["Bounded search with finite burns and automatic gravity transitions. Faster or cheaper routes may exist."], trajectory);
    }

    internal TorchFitStatus GetTorchFitStatus()
    {
        lock (gate)
        {
            var reason = world is null ? "Create or load a campaign in the System Lab to fit a drive."
                : switchingCampaign || shuttingDown ? "The campaign is changing. Try again in a moment."
                : running ? "Stop time processing in the System Lab before fitting a drive."
                : world.TorchFitBlockReason();
            return new(instanceId, revision, reason);
        }
    }

    public SessionSnapshot Execute(LabCommand command)
    {
        lock (gate)
        {
            // Stop is safe against an older snapshot in the same instance: a tick must never defeat it.
            if (shuttingDown || switchingCampaign || command.InstanceId != instanceId || command.Revision > revision
                || (command.Operation != "stop" && command.Revision != revision))
                throw new InvalidOperationException("State changed. Refresh and review before trying again.");
            if (!Guid.TryParse(command.ClientId, out _)) throw new ArgumentException("Invalid browser identity.");
            var activeWorld = RequireWorld();
            switch (command.Operation)
            {
                case "configure":
                    if (running) throw new InvalidOperationException("Stop before changing time controls.");
                    if (!Durations.Contains(command.DurationSeconds ?? -1) || command.Mode is not ("once" or "continuous"))
                        throw new ArgumentException("Choose a supported duration and mode.");
                    durationSeconds = command.DurationSeconds!.Value;
                    mode = command.Mode;
                    break;
                case "play":
                    if (running) throw new InvalidOperationException("Already processing time.");
                    interrupt = null;
                    flightNotice = null;
                    running = true;
                    stopReason = "Processing";
                    break;
                case "stop":
                    if (!running && interrupt is not null) return Read();
                    if (running) lastStopVersion = worldVersion;
                    running = false;
                    stopReason = "Player stopped";
                    break;
                case "travel":
                    if (running) throw new InvalidOperationException("Stop before issuing an order.");
                    RequireLegacyOrderAllowed(activeWorld);
                    activeWorld.OrderTravel(command.DestinationId ?? "", command.FlightSeconds ?? 21600);
                    MarkWorldChanged(significant: true);
                    stopReason = "Course committed — ready to advance";
                    break;
                case "commit-course":
                    if (running) throw new InvalidOperationException("Stop before issuing an order.");
                    if (command.PreviewId is null || !previews.TryGetValue(command.PreviewId, out var preview)
                        || preview.Revision != revision)
                        throw new InvalidOperationException("Course preview expired. Recalculate and review before issuing an order.");
                    if (!preview.Plan.UsesFiniteBurns) RequireLegacyOrderAllowed(activeWorld);
                    activeWorld.CommitTravel(preview.Plan);
                    MarkWorldChanged(significant: true);
                    stopReason = "Course committed — ready to advance";
                    break;
                case "cancel-travel":
                    if (running) throw new InvalidOperationException("Stop before cancelling a course.");
                    activeWorld.CancelTravel();
                    MarkWorldChanged(significant: true);
                    stopReason = "Course cancelled — coasting";
                    break;
                case "start-burn":
                    if (running) throw new InvalidOperationException("Stop time processing before ignition.");
                    activeWorld.StartBurn(command.BurnDirection ?? "", command.BurnSeconds ?? 0);
                    MarkWorldChanged(significant: true);
                    stopReason = "Engine ignited — ready to advance";
                    break;
                case "cut-thrust":
                    if (running) throw new InvalidOperationException("Stop time processing before cutting thrust.");
                    activeWorld.CutThrust();
                    MarkWorldChanged(significant: true);
                    stopReason = "Thrust cut — coasting";
                    break;
                case "fit-exploration-torch":
                    if (running) throw new InvalidOperationException("Stop time processing before refitting.");
                    activeWorld.FitExplorationTorch();
                    MarkWorldChanged(significant: true);
                    stopReason = "Exploration torch fitted — ready to plot a course";
                    break;
                case "debug-refuel":
                    if (running) throw new InvalidOperationException("Stop time processing before refuelling.");
                    if (!activeWorld.DebugRefuel(command.ShipId ?? "")) return Read();
                    MarkWorldChanged(significant: true);
                    stopReason = "Propellant tank refilled";
                    break;
                case "fit-designed-torch":
                    if (running) throw new InvalidOperationException("Stop time processing before fitting a drive.");
                    activeWorld.FitDesignedTorch(command.TorchDesign ?? throw new ArgumentException("Provide a torch design."));
                    MarkWorldChanged(significant: true);
                    stopReason = "Fusion torch fitted — ready to plot a course";
                    break;
                default: throw new ArgumentException("Unknown command.");
            }
            revision++;
            previews.Clear();
            return Read();
        }
    }

    public void Tick() => TickWithArrival();

    internal string? TickWithArrival()
    {
        lock (gate)
        {
            if (!running || switchingCampaign || shuttingDown) return null;
            string? arrivalNotice = null;
            try
            {
                var activeWorld = RequireWorld();
                var before = activeWorld.ElapsedTicks;
                var previousEventSequence = activeWorld.LastEvent.Sequence;
                var destination = activeWorld.CourseDestinationName;
                var deadline = checked(before + durationSeconds * TimeSpan.TicksPerSecond);
                // Movement still resolves each boundary at its own tick. Spend the rest of this interval after a notice.
                while (activeWorld.ElapsedTicks < deadline
                    && activeWorld.Advance(TimeSpan.FromTicks(deadline - activeWorld.ElapsedTicks)))
                {
                    var flightEvent = activeWorld.LastEvent;
                    if (flightEvent.ArrivalLabel is not null)
                    {
                        interrupt = flightEvent;
                        break;
                    }
                    flightNotice = flightEvent;
                }

                var recordedEvent = activeWorld.LastEvent.Sequence != previousEventSequence;
                if (recordedEvent || activeWorld.ElapsedTicks != before) MarkWorldChanged(significant: recordedEvent);
                if (interrupt is not null || mode == "once")
                {
                    running = false;
                    stopReason = interrupt?.ArrivalLabel ?? "Advance complete";
                    if (interrupt is { } arrival)
                    {
                        arrivalNotice = destination is null ? arrival.Message
                            : arrival.Kind == "course-orbit-inserted"
                                ? $"Pathfinder entered orbit at {destination}."
                                : $"Pathfinder arrived at {destination}.";
                    }
                }
            }
            catch (OverflowException)
            {
                running = false;
                stopReason = "Simulation time limit reached";
            }
            revision++;
            previews.Clear();
            return arrivalNotice;
        }
    }

    private SessionSnapshot Read() => new(instanceId, revision, running, stopReason, durationSeconds, mode,
        RequireWorld().Snapshot(), interrupt, flightNotice);

    private static void RequireLegacyOrderAllowed(SystemLab world)
    {
        if (world.HasModeledPropulsion)
            throw new InvalidOperationException("This spacecraft requires a propulsion-aware course. Use automatic course planning.");
    }
}

internal sealed class LabRunner(LabSession session, ArrivalNotifications notifications) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (session.TickWithArrival() is { } arrival) notifications.Enqueue(arrival);
        }
    }
}
