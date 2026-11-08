using Quasar.Simulation;

namespace Quasar.Server;

public sealed record LabCommand(string InstanceId, long Revision, string ClientId, string Operation,
    int? DurationSeconds = null, string? Mode = null, string? DestinationId = null, int? FlightSeconds = null);
public sealed record SessionSnapshot(string InstanceId, long Revision, bool Running, string StopReason,
    int DurationSeconds, string Mode, LabSnapshot World);

/// <summary>The host is the only writer. This gate also makes polling snapshots internally consistent.</summary>
public sealed class LabSession(TimeProvider timeProvider)
{
    private readonly object gate = new();
    private readonly SystemLab world = new();
    private readonly string instanceId = Guid.NewGuid().ToString();
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

    // The immutable celestial catalogue needs no session lock and cannot advance the world.
    public CelestialState CelestialStateAt(string bodyId, long atTicks) => world.CelestialPositions.GetState(bodyId, atTicks);

    public TrajectoryDisplay PreviewTravel(string destinationId, int flightSeconds)
    {
        lock (gate) return SystemLab.Present(world.PreviewTravel(destinationId, flightSeconds));
    }

    public CelestialState ShipStateAt(long atTicks)
    {
        lock (gate) return world.ShipStateAt(atTicks);
    }

    public SessionSnapshot Execute(LabCommand command)
    {
        lock (gate)
        {
            // Stop is safe against an older snapshot in the same instance: a tick must never defeat it.
            if (command.InstanceId != instanceId || command.Revision > revision
                || (command.Operation != "stop" && command.Revision != revision))
                throw new InvalidOperationException("State changed. Refresh and review before trying again.");
            if (!Guid.TryParse(command.ClientId, out _)) throw new ArgumentException("Invalid browser identity.");
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
                    running = false;
                    stopReason = "Player stopped";
                    break;
                case "travel":
                    if (running) throw new InvalidOperationException("Stop before issuing an order.");
                    world.OrderTravel(command.DestinationId ?? "", command.FlightSeconds ?? 21600);
                    stopReason = "Course committed — ready to advance";
                    break;
                case "cancel-travel":
                    if (running) throw new InvalidOperationException("Stop before cancelling a course.");
                    world.CancelTravel();
                    stopReason = "Course cancelled — coasting";
                    break;
                default: throw new ArgumentException("Unknown command.");
            }
            revision++;
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
            if (!running) return;
            if (timeProvider.GetUtcNow() >= leaseUntil)
            {
                running = false;
                stopReason = "Controller disconnected";
            }
            else
            {
                try
                {
                    var arrived = world.Advance(TimeSpan.FromSeconds(durationSeconds));
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
        }
    }

    private SessionSnapshot Read() => new(instanceId, revision, running, stopReason, durationSeconds, mode, world.Snapshot());
}

public sealed class LabRunner(LabSession session) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken)) session.Tick();
    }
}
