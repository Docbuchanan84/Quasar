using Microsoft.Extensions.Logging.Abstractions;
using Quasar.Server;
using Quasar.Server.Persistence;
using Quasar.Simulation;

namespace Quasar.Simulation.Tests;

public sealed class CampaignLifecycleTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("quasar-lifecycle-").FullName;
    private readonly Clock clock = new();
    private readonly string client = Guid.NewGuid().ToString();

    [Fact]
    public void DefaultAutosaveUsesRealElapsedTimeAndSkipsUnchangedState()
    {
        var (session, lifecycle, store) = Start(new());
        var campaign = lifecycle.Create(Version(session), "Travel").Campaign!;
        var initial = store.GetCampaign(campaign.Id);

        Advance(session, seconds: 2592000);
        clock.Advance(TimeSpan.FromSeconds(299));
        lifecycle.CheckAutosave();
        Assert.Equal(initial.HeadSequence, store.GetCampaign(campaign.Id).HeadSequence);
        Assert.True(session.CampaignStatus().Dirty);

        clock.Advance(TimeSpan.FromSeconds(1));
        lifecycle.CheckAutosave();
        Assert.Equal(initial.HeadSequence + 1, store.GetCampaign(campaign.Id).HeadSequence);
        Assert.False(session.CampaignStatus().Dirty);

        session.PreviewCourse("planet-earth", 21600);
        Command(session, "configure", duration: 1);
        clock.Advance(TimeSpan.FromHours(1));
        lifecycle.CheckAutosave();
        Assert.Equal(initial.HeadSequence + 1, store.GetCampaign(campaign.Id).HeadSequence);
    }

    [Fact]
    public void SettingsSurviveRestartAndApplyRetentionWithoutPruningManualSaves()
    {
        var path = Path.Combine(root, "persistence.json");
        File.WriteAllText(path, """{"autosave":{"intervalSeconds":11,"recoveryCheckpoints":2},"saveOnShutdown":false}""");
        var (session, lifecycle, store) = Start(PersistenceSettings.Read(path));
        var campaign = lifecycle.Create(Version(session), "Retention").Campaign!;
        var manual = lifecycle.Save(Version(session), "Keep forever");
        Advance(session);
        clock.Advance(TimeSpan.FromSeconds(11));
        lifecycle.CheckAutosave();

        var (restarted, restartedLifecycle, _) = Start(PersistenceSettings.Read(path));
        Advance(restarted);
        clock.Advance(TimeSpan.FromSeconds(10));
        restartedLifecycle.CheckAutosave();
        Assert.Equal(3, store.GetCampaign(campaign.Id).HeadSequence);
        clock.Advance(TimeSpan.FromSeconds(1));
        restartedLifecycle.CheckAutosave();
        Advance(restarted);
        clock.Advance(TimeSpan.FromSeconds(11));
        restartedLifecycle.CheckAutosave();

        var saves = store.ListCheckpoints(campaign.Id);
        Assert.Equal(new long[] { 5, 4, 2 }, saves.Select(checkpoint => checkpoint.Sequence));
        Assert.Contains(manual, saves);
    }

    [Fact]
    public void OptInEventsCoalesceAndDoNotSaveCleanWorlds()
    {
        var settings = new PersistenceSettings
        {
            Autosave = new() { SaveOnStop = true, SaveOnSignificantEvent = true }
        };
        var (session, lifecycle, store) = Start(settings);
        var campaign = lifecycle.Create(Version(session), "Triggered").Campaign!;
        Command(session, "travel", destination: "planet-earth");
        Command(session, "cancel-travel");
        lifecycle.CheckAutosave();
        Assert.Equal(1, store.GetCampaign(campaign.Id).HeadSequence);

        clock.Advance(TimeSpan.FromSeconds(5));
        lifecycle.CheckAutosave();
        Assert.Equal(2, store.GetCampaign(campaign.Id).HeadSequence);

        Command(session, "configure", duration: 1, mode: "continuous");
        Command(session, "play");
        session.Tick();
        Command(session, "stop");
        lifecycle.CheckAutosave();
        Assert.Equal(2, store.GetCampaign(campaign.Id).HeadSequence);

        clock.Advance(TimeSpan.FromSeconds(5));
        lifecycle.CheckAutosave();
        Assert.Equal(3, store.GetCampaign(campaign.Id).HeadSequence);
        clock.Advance(TimeSpan.FromSeconds(5));
        lifecycle.CheckAutosave();
        Assert.Equal(3, store.GetCampaign(campaign.Id).HeadSequence);
    }

    [Fact]
    public async Task CommandsDuringDiskWriteStayDirtyAndSubsequentSavesCaptureThemInOrder()
    {
        var store = new SqliteCampaignStore(root);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new BlockingStore(store, entered, release);
        var (session, lifecycle, _) = Start(new(), blocking);
        var campaign = lifecycle.Create(Version(session), "Concurrent").Campaign!;

        Advance(session);
        var first = Task.Run(() => lifecycle.Save(Version(session), "First"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Run(() => Advance(session))
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); }

        var saved = await first;
        Assert.True(session.CampaignStatus().Dirty);
        Assert.Equal(TimeSpan.TicksPerSecond, saved.SimulationTicks);
        var newer = lifecycle.Save(Version(session), "Second");
        Assert.Equal(saved.Sequence + 1, newer.Sequence);
        Assert.Equal(2 * TimeSpan.TicksPerSecond, newer.SimulationTicks);
        Assert.Equal(newer, store.LoadLatestValidCheckpoint(campaign.Id).Checkpoint!.Info);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"autosave":{"intervalSeconds":0}}""")]
    [InlineData("""{"autosave":{"recoveryCheckpoints":101}}""")]
    [InlineData("""{"autosave":null}""")]
    [InlineData("""{"saveOnShutdown":true,"saveOnShutdown":false}""")]
    [InlineData("""{"autosave":{"unknown":true}}""")]
    public void InvalidSettingsStopHostConstructionWithoutWritingCampaigns(string badSettings)
    {
        var (session, lifecycle, _) = Start(new());
        var campaign = lifecycle.Create(Version(session), "Preserved").Campaign!;
        var database = Path.Combine(root, campaign.Id.ToString("N") + ".db");
        var bytes = File.ReadAllBytes(database);
        var path = Path.Combine(root, "persistence.json");
        File.WriteAllText(path, badSettings);

        Assert.Throws<InvalidDataException>(() => QuasarHost.Build(
            ["--contentRoot", root, "--Persistence:DataRoot", root, "--Persistence:SettingsFile", path]));
        Assert.Equal(bytes, File.ReadAllBytes(database));
        Assert.Equal(badSettings, File.ReadAllText(path));
    }

    [Fact]
    public void OldLeaseCannotExtendRunningTimeAfterCampaignReplacement()
    {
        var (session, lifecycle, _) = Start(new());
        var active = lifecycle.Create(Version(session), "Lease").Campaign!;
        var original = session.Snapshot();
        var oldPreview = session.PreviewCourse("planet-earth", 21600);

        Command(session, "play");
        var loaded = lifecycle.Continue(Version(session), active.Id);
        Assert.False(loaded.Running);
        Assert.NotEqual(original.InstanceId, loaded.InstanceId);
        Assert.Throws<InvalidOperationException>(() =>
            session.Execute(new(original.InstanceId, original.Revision, client, "stop")));
        Assert.Throws<InvalidOperationException>(() =>
            session.Execute(new(loaded.InstanceId, loaded.Revision, client, "commit-course", PreviewId: oldPreview.PreviewId)));

        Command(session, "configure", duration: 1, mode: "continuous");
        Command(session, "play");
        clock.Advance(TimeSpan.FromSeconds(4));
        session.RenewLease(original.InstanceId, client);
        clock.Advance(TimeSpan.FromSeconds(1));
        session.Tick();
        Assert.Equal("Controller disconnected", session.Snapshot().StopReason);
        Assert.Equal(0, session.Snapshot().World.ElapsedTicks);
    }

    private (LabSession, CampaignLifecycle, SqliteCampaignStore) Start(PersistenceSettings settings, ICampaignStore? overrideStore = null)
    {
        var session = new LabSession(clock, startWithWorld: false);
        var store = new SqliteCampaignStore(root, settings.Autosave.RecoveryCheckpoints);
        var lifecycle = new CampaignLifecycle(session, overrideStore ?? store, settings, clock,
            NullLogger<CampaignLifecycle>.Instance, root);
        lifecycle.Initialize();
        return (session, lifecycle, store);
    }

    private void Advance(LabSession session, int seconds = 1)
    {
        Command(session, "configure", duration: seconds);
        Command(session, "play");
        session.Tick();
    }

    private void Command(LabSession session, string operation, int? duration = null, string mode = "once", string? destination = null)
    {
        var current = session.Snapshot();
        session.Execute(new(current.InstanceId, current.Revision, client, operation, duration, mode, destination));
    }

    private static SessionVersion Version(LabSession session)
    {
        var status = session.CampaignStatus();
        return new(status.InstanceId, status.Revision);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
        public void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }

    private sealed class BlockingStore(ICampaignStore inner, TaskCompletionSource entered, ManualResetEventSlim release) : ICampaignStore
    {
        public CheckpointInfo SaveNamed(Guid campaignId, long expectedHead, string label, string gameBuild, SystemLabState state)
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test did not release the store.");
            return inner.SaveNamed(campaignId, expectedHead, label, gameBuild, state);
        }

        public CampaignInfo CreateCampaign(string displayName) => inner.CreateCampaign(displayName);
        public CampaignInfo CreateCampaign(string displayName, string gameBuild, SystemLabState initialState) =>
            inner.CreateCampaign(displayName, gameBuild, initialState);
        public IReadOnlyList<CampaignDiscovery> ListCampaigns() => inner.ListCampaigns();
        public CampaignInfo GetCampaign(Guid campaignId) => inner.GetCampaign(campaignId);
        public void MigrateCampaign(Guid campaignId) => inner.MigrateCampaign(campaignId);
        public IReadOnlyList<CheckpointInfo> ListCheckpoints(Guid campaignId) => inner.ListCheckpoints(campaignId);
        public CheckpointDiscovery DiscoverCheckpoints(Guid campaignId) => inner.DiscoverCheckpoints(campaignId);
        public CheckpointInfo SaveRecovery(Guid campaignId, long expectedHead, string gameBuild, SystemLabState state) =>
            inner.SaveRecovery(campaignId, expectedHead, gameBuild, state);
        public LoadedCheckpoint LoadCheckpoint(Guid campaignId, Guid checkpointId) => inner.LoadCheckpoint(campaignId, checkpointId);
        public CheckpointRecovery LoadLatestValidCheckpoint(Guid campaignId) => inner.LoadLatestValidCheckpoint(campaignId);
        public CampaignBackup CreateBackup(Guid campaignId) => inner.CreateBackup(campaignId);
        public IReadOnlyList<CampaignBackup> ListBackups(Guid campaignId) => inner.ListBackups(campaignId);
        public CampaignInfo RestoreBackup(Guid campaignId, Guid backupId, string displayName) =>
            inner.RestoreBackup(campaignId, backupId, displayName);
    }
}
