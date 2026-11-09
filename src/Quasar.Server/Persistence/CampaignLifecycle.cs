using System.Reflection;
using Microsoft.Data.Sqlite;
using Quasar.Simulation;

namespace Quasar.Server.Persistence;

public sealed class CampaignLifecycle(LabSession session, ICampaignStore store, PersistenceSettings settings,
    TimeProvider clock, ILogger<CampaignLifecycle> logger, string dataRoot)
{
    private static readonly string GameBuild = typeof(CampaignLifecycle).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    private static readonly TimeSpan EventCooldown = TimeSpan.FromSeconds(5);
    // All saves and replacements use this writer. Simulation commands use only the session gate.
    private readonly object writer = new();
    private readonly string lastPlayedPath = Path.Combine(dataRoot, "last-played");
    private long lastRecoveryAttempt = clock.GetTimestamp();

    public void Initialize()
    {
        lock (writer)
        {
            try
            {
                var recorded = ReadLastPlayed();
                var lastPlayed = recorded;
                if (lastPlayed is null)
                {
                    var entry = store.ListCampaigns().FirstOrDefault(entry => entry.Campaign?.HeadSequence > 0
                        || entry.Error is not null);
                    if (entry is null) return;
                    lastPlayed = entry.Id;
                }

                var selection = Recover(lastPlayed.Value);
                Activate(selection, writeMarker: selection.Campaign.Id != recorded);
            }
            catch (Exception error) when (IsStorageFailure(error) || error is CheckpointConflictException or KeyNotFoundException)
            {
                logger.LogError(error, "Could not recover the last played campaign");
                session.ReportRecoveryFailure("The last played campaign could not be recovered. Choose an existing save or campaign.");
            }
        }
    }

    public CampaignSessionStatus Create(SessionVersion expected, string displayName) => Switch(expected, () =>
    {
        var info = store.CreateCampaign(displayName, GameBuild, new SystemLab().ExportState());
        return Recover(info.Id);
    });

    public CampaignSessionStatus Load(SessionVersion expected, Guid campaignId, Guid checkpointId) => Switch(expected, () =>
    {
        var info = store.GetCampaign(campaignId);
        var checkpoint = store.LoadCheckpoint(campaignId, checkpointId);
        RequireUnchangedHead(info);
        return new(info, checkpoint);
    });

    public CampaignSessionStatus Continue(SessionVersion expected, Guid campaignId) => Switch(expected, () => Recover(campaignId));

    private CampaignSessionStatus Switch(SessionVersion expected, Func<CampaignSelection> prepare)
    {
        lock (writer)
        {
            session.BeginCampaignSwitch(expected);
            try
            {
                Activate(prepare());
                return session.CampaignStatus();
            }
            finally
            {
                session.EndCampaignSwitch();
            }
        }
    }

    public CheckpointInfo Save(SessionVersion expected, string label)
    {
        lock (writer)
        {
            var capture = session.CaptureManualSave(expected);
            var saved = store.SaveNamed(capture.Campaign.Id, capture.Campaign.HeadSequence, label, GameBuild, capture.State);
            session.RecordSaved(capture, saved);
            return saved;
        }
    }

    public void CheckAutosave()
    {
        if (!settings.Autosave.Enabled || !Monitor.TryEnter(writer))
            return;
        try
        {
            var elapsed = clock.GetElapsedTime(lastRecoveryAttempt);
            var due = elapsed >= TimeSpan.FromSeconds(settings.Autosave.IntervalSeconds);
            if (!due && elapsed < EventCooldown) return;

            var capture = session.CaptureDirtySave(due, settings.Autosave);
            if (capture is null) return;
            try { SaveRecovery(capture); }
            finally { lastRecoveryAttempt = clock.GetTimestamp(); }
        }
        catch (Exception error) when (IsStorageFailure(error) || error is CheckpointConflictException)
        {
            session.ReportAutosaveFailure(error is CheckpointConflictException
                ? "The stored campaign changed. Reload it before saving again."
                : "Autosave failed. Check campaign storage before relying on recovery.");
            logger.LogWarning(error, "Autosave failed for the active campaign");
        }
        finally
        {
            Monitor.Exit(writer);
        }
    }

    internal void SaveOnShutdown(CancellationToken cancellationToken)
    {
        if (!settings.SaveOnShutdown) return;
        lock (writer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = session.CaptureDirtySave(periodic: true, settings.Autosave);
            if (capture is null) return;
            try { SaveRecovery(capture); }
            catch (Exception error) when (IsStorageFailure(error) || error is CheckpointConflictException)
            {
                logger.LogWarning(error, "Final campaign checkpoint failed");
            }
        }
    }

    private void SaveRecovery(CampaignCapture capture)
    {
        var checkpoint = store.SaveRecovery(capture.Campaign.Id, capture.Campaign.HeadSequence, GameBuild, capture.State);
        session.RecordSaved(capture, checkpoint);
    }

    private CampaignSelection Recover(Guid campaignId)
    {
        CampaignInfo? info = null;
        var rejected = 0;
        Exception? failure = null;
        try
        {
            info = store.GetCampaign(campaignId);
            var latest = store.LoadLatestValidCheckpoint(campaignId);
            RequireUnchangedHead(info);
            rejected = latest.RejectedCount;
            if (latest.Checkpoint is { } checkpoint)
                return new(info, checkpoint, rejected);
        }
        catch (Exception error) when (IsInvalidData(error) || error is FileNotFoundException)
        {
            failure = error;
        }

        foreach (var backup in store.ListBackups(campaignId))
        {
            try
            {
                var recovered = store.RestoreBackup(campaignId, backup.Id, info?.DisplayName ?? "Recovered campaign");
                var latest = store.LoadLatestValidCheckpoint(recovered.Id);
                if (latest.Checkpoint is { } checkpoint)
                    return new(recovered, checkpoint, rejected, campaignId);
            }
            catch (Exception error) when (IsInvalidData(error) || error is FileNotFoundException)
            {
                logger.LogWarning(error, "Rejected backup {BackupId} for campaign {CampaignId}", backup.Id, campaignId);
            }
        }

        if (failure is FileNotFoundException) throw failure;
        if (info?.HeadSequence == 0 && rejected == 0)
            throw new KeyNotFoundException("The campaign has no checkpoints.");
        throw new InvalidDataException("No valid campaign checkpoint or backup could be recovered.", failure);
    }

    private void RequireUnchangedHead(CampaignInfo info)
    {
        var current = store.GetCampaign(info.Id);
        if (current.HeadSequence != info.HeadSequence)
            throw new CheckpointConflictException(info.HeadSequence, current.HeadSequence);
    }

    private void Activate(CampaignSelection selection, bool writeMarker = true)
    {
        if (writeMarker) WriteLastPlayed(selection.Campaign.Id);
        session.InstallCampaign(selection.Campaign, selection.Checkpoint, selection.Rejected, selection.RecoveredFrom);
        lastRecoveryAttempt = clock.GetTimestamp();
    }

    private Guid? ReadLastPlayed()
    {
        SqliteCampaignStore.RequirePlainPath(dataRoot);
        if (File.Exists(dataRoot))
            throw new InvalidDataException("The campaign storage root must be a directory.");
        SqliteCampaignStore.RequirePlainPath(lastPlayedPath);
        try
        {
            using var marker = File.OpenRead(lastPlayedPath);
            if (marker.Length == 32)
            {
                using var reader = new StreamReader(marker);
                if (Guid.TryParseExact(reader.ReadToEnd(), "N", out var id) && id != Guid.Empty)
                    return id;
            }
            throw new InvalidDataException("Invalid last-played campaign marker.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private void WriteLastPlayed(Guid campaignId)
    {
        SqliteCampaignStore.RequirePlainPath(lastPlayedPath);
        var temporary = Path.Combine(dataRoot, $".last-played-{Guid.NewGuid():N}");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes(campaignId.ToString("N")));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, lastPlayedPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static bool IsInvalidData(Exception error) => error is InvalidDataException
        or SqliteException { SqliteErrorCode: 1 or 11 or 26 };

    internal static bool IsStorageFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or SqliteException or InvalidDataException;

    private sealed record CampaignSelection(CampaignInfo Campaign, LoadedCheckpoint Checkpoint, int Rejected = 0,
        Guid? RecoveredFrom = null);
}

internal sealed class CampaignRunner(CampaignLifecycle lifecycle, LabSession session, TimeProvider clock,
    ILogger<CampaignRunner> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        lifecycle.Initialize();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            lifecycle.CheckAutosave();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(7));
        var token = deadline.Token;
        try
        {
            await base.StopAsync(token);
            await Task.Run(() =>
            {
                session.Quiesce();
                lifecycle.SaveOnShutdown(token);
            }, token).WaitAsync(token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            logger.LogWarning("The host shutdown deadline expired while saving the campaign");
        }
    }
}
