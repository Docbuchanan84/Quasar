using Quasar.Simulation;

namespace Quasar.Server.Persistence;

public enum CheckpointKind
{
    Named = 1,
    Recovery = 2
}

public sealed record CampaignInfo(Guid Id, string DisplayName, DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc, long HeadSequence);

public sealed record CampaignDiscovery(Guid Id, CampaignInfo? Campaign, string? Error);

public sealed record CheckpointInfo(Guid Id, long Sequence, CheckpointKind Kind, string? Label,
    DateTimeOffset CreatedUtc, string GameBuild, long SimulationTicks, int StateFormatVersion, string Sha256);

public sealed record LoadedCheckpoint(CheckpointInfo Info, SystemLabState State, SystemLab World);

public sealed record CheckpointRecovery(LoadedCheckpoint? Checkpoint, int RejectedCount);

public sealed record CampaignBackup(Guid Id, DateTimeOffset LastWriteUtc);

public sealed class CheckpointConflictException(long expected, long actual)
    : InvalidOperationException($"Expected campaign sequence {expected}, found {actual}.")
{
    public long ExpectedSequence { get; } = expected;
    public long ActualSequence { get; } = actual;
}

public interface ICampaignStore
{
    CampaignInfo CreateCampaign(string displayName);
    IReadOnlyList<CampaignDiscovery> ListCampaigns();
    CampaignInfo GetCampaign(Guid campaignId);
    void MigrateCampaign(Guid campaignId);

    IReadOnlyList<CheckpointInfo> ListCheckpoints(Guid campaignId);
    CheckpointInfo SaveNamed(Guid campaignId, long expectedHead, string label, string gameBuild, SystemLabState state);
    CheckpointInfo SaveRecovery(Guid campaignId, long expectedHead, string gameBuild, SystemLabState state);
    LoadedCheckpoint LoadCheckpoint(Guid campaignId, Guid checkpointId);
    CheckpointRecovery LoadLatestValidCheckpoint(Guid campaignId);

    CampaignBackup CreateBackup(Guid campaignId);
    IReadOnlyList<CampaignBackup> ListBackups(Guid campaignId);
    CampaignInfo RestoreBackup(Guid campaignId, Guid backupId, string displayName);
}
