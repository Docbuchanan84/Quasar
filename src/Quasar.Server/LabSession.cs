using Quasar.Simulation;

namespace Quasar.Server;

public sealed record LabCommand(string InstanceId, long Revision, string ClientId, string Operation,
    int? DurationSeconds = null, string? Mode = null, string? DestinationId = null, int? FlightSeconds = null,
    string? PreviewId = null);
public sealed record CoursePreview(string PreviewId, string InstanceId, long Revision, string ShipId,
    string DestinationId, double SampledDistanceKm, string[] Warnings, TrajectoryDisplay Trajectory);
public sealed record SessionSnapshot(string InstanceId, long Revision, bool Running, string StopReason,
    int DurationSeconds, string Mode, LabSnapshot World);

/// <summary>The host is the only writer. This gate also makes polling snapshots internally consistent.</summary>
public sealed partial class LabSession(TimeProvider timeProvider, bool startWithWorld = true)
{
    private readonly object gate = new();
    private SystemLab? world = startWithWorld ? new() : null;
    private readonly Dictionary<string, (long Revision, TrajectoryPlan Plan)> previews = [];
    private string instanceId = Guid.NewGuid().ToString();
    private long revision;
    private bool running;
    private string stopReason = "Ready";
    private int durationSeconds = 3600;
    private string mode = "once";
    private string? controller;
    private DateTimeOffset leaseUntil;
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

    public CelestialState ShipStateAt(long atTicks)
    {
        lock (gate) return RequireWorld().ShipStateAt(atTicks);
    }

    public CoursePreview PreviewCourse(string destinationId, int flightSeconds)
    {
        lock (gate)
        {
            if (running) throw new InvalidOperationException("Stop time processing before plotting a course.");
            var plan = RequireWorld().PreviewTravel(destinationId, flightSeconds);
            var trajectory = SystemLab.Present(plan);
            var distance = trajectory.Points.Zip(trajectory.Points.Skip(1),
                (a, b) => a.PositionKm.DistanceTo(b.PositionKm)).Sum();
            // Bounded, disposable tokens retain solved plans, never client-provided geometry.
            if (previews.Count >= 32) previews.Remove(previews.Keys.First());
            var id = Guid.NewGuid().ToString();
            previews.Add(id, (revision, plan));
            return new(id, instanceId, revision, SystemLab.ShipId, destinationId, distance,
                ["Impulsive transfer in one gravitational well; no finite thrust or body-radius collision model.",
                 "Delta-v is a calculated requirement, not a verified spacecraft capability."], trajectory);
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
                    controller = command.ClientId;
                    leaseUntil = timeProvider.GetUtcNow().AddSeconds(5);
                    running = true;
                    stopReason = "Processing";
                    break;
                case "stop":
                    if (running) lastStopVersion = worldVersion;
                    running = false;
                    stopReason = "Player stopped";
                    break;
                case "travel":
                    if (running) throw new InvalidOperationException("Stop before issuing an order.");
                    activeWorld.OrderTravel(command.DestinationId ?? "", command.FlightSeconds ?? 21600);
                    MarkWorldChanged(significant: true);
                    stopReason = "Course committed — ready to advance";
                    break;
                case "commit-course":
                    if (running) throw new InvalidOperationException("Stop before issuing an order.");
                    if (command.PreviewId is null || !previews.TryGetValue(command.PreviewId, out var preview)
                        || preview.Revision != revision)
                        throw new InvalidOperationException("Course preview expired. Recalculate and review before issuing an order.");
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
                default: throw new ArgumentException("Unknown command.");
            }
            revision++;
            previews.Clear();
            return Read();
        }
    }

    public void RenewLease(string instance, string client)
    {
        lock (gate)
        {
            if (instance == instanceId && client == controller && running)
                leaseUntil = timeProvider.GetUtcNow().AddSeconds(5);
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            if (!running || switchingCampaign || shuttingDown) return;
            if (timeProvider.GetUtcNow() >= leaseUntil)
            {
                running = false;
                stopReason = "Controller disconnected";
            }
            else
            {
                try
                {
                    var activeWorld = RequireWorld();
                    var before = activeWorld.ElapsedTicks;
                    var arrived = activeWorld.Advance(TimeSpan.FromSeconds(durationSeconds));
                    if (arrived || activeWorld.ElapsedTicks != before) MarkWorldChanged(significant: arrived);
                    if (arrived || mode == "once")
                    {
                        running = false;
                        stopReason = arrived ? "Arrival — awaiting orders" : "Advance complete";
                    }
                }
                catch (OverflowException)
                {
                    running = false;
                    stopReason = "Simulation time limit reached";
                }
            }
            revision++;
            previews.Clear();
        }
    }

    private SessionSnapshot Read() => new(instanceId, revision, running, stopReason, durationSeconds, mode, RequireWorld().Snapshot());
}

public sealed class LabRunner(LabSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken)) session.Tick();
    }
}
