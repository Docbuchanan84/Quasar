using Quasar.Server.Persistence;
using Quasar.Simulation;

namespace Quasar.Server;

public sealed record SessionVersion(string InstanceId, long Revision);
public sealed record CampaignSessionStatus(string InstanceId, long Revision, bool Running, bool Dirty,
    CampaignInfo? Campaign, CheckpointInfo? LastCheckpoint, int RejectedCheckpoints, Guid? RecoveredFromCampaignId,
    string? RecoveryError, string? AutosaveError);

internal sealed record CampaignCapture(CampaignInfo Campaign, long WorldVersion, SystemLabState State);
internal sealed class CampaignUnavailableException(string message, bool recoveryFailed) : Exception(message)
{
    public bool RecoveryFailed { get; } = recoveryFailed;
}

public sealed partial class LabSession
{
    private CampaignInfo? campaign;
    private CheckpointInfo? lastCheckpoint;
    private int rejectedCheckpoints;
    private Guid? recoveredFromCampaignId;
    private string? recoveryError;
    private string? autosaveError;
    private long worldVersion;
    private long savedVersion;
    private long lastStopVersion;
    private long lastSignificantVersion;
    private bool switchingCampaign;
    private bool shuttingDown;

    public CampaignSessionStatus CampaignStatus()
    {
        lock (gate)
            return new(instanceId, revision, running, worldVersion != savedVersion, campaign, lastCheckpoint,
                rejectedCheckpoints, recoveredFromCampaignId, recoveryError, autosaveError);
    }

    internal void ReportRecoveryFailure(string message)
    {
        lock (gate) recoveryError = message;
    }

    internal void ReportAutosaveFailure(string message)
    {
        lock (gate) autosaveError = message;
    }

    internal void BeginCampaignSwitch(SessionVersion expected)
    {
        lock (gate)
        {
            RequireCurrent(expected);
            switchingCampaign = true;
        }
    }

    internal void EndCampaignSwitch()
    {
        lock (gate) switchingCampaign = false;
    }

    internal void InstallCampaign(CampaignInfo info, LoadedCheckpoint checkpoint, int rejected, Guid? recoveredFrom)
    {
        lock (gate)
        {
            world = checkpoint.World;
            campaign = info;
            lastCheckpoint = checkpoint.Info;
            rejectedCheckpoints = rejected;
            recoveredFromCampaignId = recoveredFrom;
            recoveryError = null;
            autosaveError = null;
            worldVersion = savedVersion = lastStopVersion = lastSignificantVersion = 0;
            instanceId = Guid.NewGuid().ToString();
            revision = 0;
            running = false;
            stopReason = world.LastEvent.ArrivalLabel ?? "Campaign loaded — paused";
            durationSeconds = 3600;
            mode = "once";
            interrupt = world.LastEvent.ArrivalLabel is not null && world.LastEvent.AtTicks == world.ElapsedTicks
                ? world.LastEvent : null;
            previews.Clear();
        }
    }

    internal CampaignCapture CaptureManualSave(SessionVersion expected)
    {
        lock (gate)
        {
            RequireCurrent(expected);
            var activeWorld = RequireWorld();
            if (campaign is null)
                throw new CampaignUnavailableException("Create or load a campaign before saving.", recoveryFailed: false);
            return new(campaign, worldVersion, activeWorld.ExportState());
        }
    }

    internal CampaignCapture? CaptureDirtySave(bool periodic, AutosaveSettings options)
    {
        lock (gate)
        {
            if (campaign is null || worldVersion == savedVersion)
                return null;
            if (!periodic && !(options.SaveOnStop && lastStopVersion > savedVersion)
                && !(options.SaveOnSignificantEvent && lastSignificantVersion > savedVersion))
                return null;
            return new(campaign, worldVersion, RequireWorld().ExportState());
        }
    }

    // The persistence writer serializes captures, commits and campaign exchanges.
    internal void RecordSaved(CampaignCapture capture, CheckpointInfo checkpoint)
    {
        lock (gate)
        {
            campaign = capture.Campaign with { HeadSequence = checkpoint.Sequence, UpdatedUtc = checkpoint.CreatedUtc };
            savedVersion = capture.WorldVersion;
            lastCheckpoint = checkpoint;
            autosaveError = null;
        }
    }

    internal void Quiesce()
    {
        lock (gate)
        {
            shuttingDown = true;
            running = false;
            stopReason = "Host stopping";
        }
    }

    private void RequireCurrent(SessionVersion expected)
    {
        if (shuttingDown || switchingCampaign || expected.InstanceId != instanceId || expected.Revision != revision)
            throw new InvalidOperationException("State changed. Refresh and review before trying again.");
    }

    private SystemLab RequireWorld() => world
        ?? throw new CampaignUnavailableException(recoveryError ?? "Create or load a campaign to play.", recoveryError is not null);

    private void MarkWorldChanged(bool significant)
    {
        worldVersion++;
        if (significant) lastSignificantVersion = worldVersion;
    }
}
