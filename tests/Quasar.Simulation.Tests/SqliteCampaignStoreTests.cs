using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Quasar.Server.Persistence;

namespace Quasar.Simulation.Tests;

public sealed class SqliteCampaignStoreTests : IDisposable
{
    private const string Build = "test-build";
    private readonly string root = Path.Combine(Path.GetTempPath(), "Quasar-storage-tests", Guid.NewGuid().ToString("N"));
    private SqliteCampaignStore Store(int retention = SqliteCampaignStore.DefaultRecoveryRetention) => new(root, retention);

    [Fact]
    public void CampaignsAndNamedMidflightSavesRoundTripExactly()
    {
        var store = Store();
        Assert.Empty(store.ListCampaigns());
        Assert.False(Directory.Exists(root));
        var campaign = store.CreateCampaign("Pathfinder");
        var other = store.CreateCampaign("Pathfinder");
        Assert.NotEqual(campaign.Id, other.Id);
        Assert.Equal(campaign, store.GetCampaign(campaign.Id));
        Assert.Equal(2, store.ListCampaigns().Count);

        var lab = new SystemLab();
        lab.OrderTravel("planet-iris");
        lab.Advance(TimeSpan.FromHours(2));
        var state = lab.ExportState();
        var payload = SimulationStateJson.Serialize(state);
        var checkpoint = store.SaveNamed(campaign.Id, 0, "Midflight 'α'", Build, state);

        var reopened = Store();
        var loaded = reopened.LoadCheckpoint(campaign.Id, checkpoint.Id);
        Assert.Equal(checkpoint, loaded.Info);
        Assert.Equal(1L, checkpoint.Sequence);
        Assert.Equal(CheckpointKind.Named, checkpoint.Kind);
        Assert.Equal("Midflight 'α'", checkpoint.Label);
        Assert.Equal(Build, checkpoint.GameBuild);
        Assert.Equal(lab.ElapsedTicks, checkpoint.SimulationTicks);
        Assert.Equal(SimulationState.CurrentFormatVersion, checkpoint.StateFormatVersion);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), checkpoint.Sha256);
        Assert.Equal(checkpoint.CreatedUtc, reopened.GetCampaign(campaign.Id).UpdatedUtc);
        Assert.Equal(TimeSpan.Zero, checkpoint.CreatedUtc.Offset);
        Assert.Equal(payload, ReadStoredPayload(campaign.Id));
        Assert.Equal(payload, SimulationStateJson.Serialize(loaded.State));
        Assert.Equal(payload, SimulationStateJson.Serialize(loaded.World.ExportState()));
        Assert.Empty(reopened.ListCheckpoints(other.Id));
        Assert.Null(reopened.LoadLatestValidCheckpoint(other.Id).Checkpoint);
        Assert.Throws<KeyNotFoundException>(() => reopened.LoadCheckpoint(other.Id, checkpoint.Id));

        Assert.True(lab.Advance(TimeSpan.FromDays(15)));
        Assert.True(loaded.World.Advance(TimeSpan.FromDays(15)));
        Assert.Equal(lab.ElapsedTicks, loaded.World.ElapsedTicks);
        Assert.Equal(lab.Snapshot().Events, loaded.World.Snapshot().Events);
        Assert.Equal(lab.ShipStateAt(lab.ElapsedTicks), loaded.World.ShipStateAt(loaded.World.ElapsedTicks));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void SignedInt64TicksAreLosslessInSqliteAndThePayload(long ticks)
    {
        var store = Store();
        var campaign = store.CreateCampaign("Signed epochs");
        var state = new SystemLab().ExportState();
        var ship = state.Simulation.Spacecraft[0];
        state = state with
        {
            Simulation = state.Simulation with
            {
                ElapsedTicks = ticks,
                Spacecraft = [ship with { FreeCoast = ship.FreeCoast! with { EpochTicks = ticks } }]
            },
            History = [state.History[0] with { AtTicks = ticks }]
        };

        var saved = store.SaveRecovery(campaign.Id, 0, Build, state);
        var loaded = store.LoadCheckpoint(campaign.Id, saved.Id);
        Assert.Equal(ticks, saved.SimulationTicks);
        Assert.Equal(ticks, Scalar(campaign.Id, "SELECT simulation_ticks FROM checkpoints;"));
        Assert.Equal(ticks, loaded.World.ElapsedTicks);
        Assert.Equal(ticks, loaded.State.History[0].AtTicks);
        Assert.Equal(SimulationStateJson.Serialize(state), ReadStoredPayload(campaign.Id));
    }

    [Fact]
    public void RecoveryRotationRetainsNamedSavesAndAppliesConfigurationOnCommit()
    {
        var store = Store();
        var campaign = store.CreateCampaign("Rotation");
        var state = new SystemLab().ExportState();
        var named = store.SaveNamed(campaign.Id, 0, "Keep", Build, state);
        for (var head = 1; head <= 6; head++)
            store.SaveRecovery(campaign.Id, head, Build, state);

        Assert.Equal(new long[] { 7, 6, 5, 4, 3, 1 }, store.ListCheckpoints(campaign.Id).Select(info => info.Sequence));
        var shortRetention = Store(2);
        Assert.Equal(6, shortRetention.ListCheckpoints(campaign.Id).Count);
        shortRetention.SaveRecovery(campaign.Id, 7, Build, state);
        Assert.Equal(new long[] { 8, 7, 1 }, store.ListCheckpoints(campaign.Id).Select(info => info.Sequence));

        store.SaveRecovery(campaign.Id, 8, Build, state);
        Assert.Equal(new long[] { 9, 8, 7, 1 }, store.ListCheckpoints(campaign.Id).Select(info => info.Sequence));
        Assert.Equal(named, store.LoadCheckpoint(campaign.Id, named.Id).Info);
    }

    [Fact]
    public void RotationFailureRollsBackInsertAndHeadUpdate()
    {
        var store = Store(1);
        var campaign = store.CreateCampaign("Atomic");
        var state = new SystemLab().ExportState();
        var original = store.SaveRecovery(campaign.Id, 0, Build, state);
        var before = store.GetCampaign(campaign.Id);
        Execute(campaign.Id, """
            CREATE TRIGGER fail_rotation BEFORE DELETE ON checkpoints
            BEGIN SELECT RAISE(ABORT, 'injected failure during rotation'); END;
            """);

        var error = Assert.Throws<SqliteException>(() => store.SaveRecovery(campaign.Id, 1, Build, state));
        Assert.Contains("injected failure", error.Message);
        Assert.Equal(before, Store().GetCampaign(campaign.Id));
        Assert.Equal(original, Assert.Single(Store().ListCheckpoints(campaign.Id)));
        Assert.Equal(original, Store().LoadLatestValidCheckpoint(campaign.Id).Checkpoint!.Info);

        Execute(campaign.Id, "DROP TRIGGER fail_rotation;");
        Assert.Equal(2L, store.SaveRecovery(campaign.Id, 1, Build, state).Sequence);
    }

    [Fact]
    public async Task SeparateStoreInstancesCannotCommitTheSameExpectedHead()
    {
        var campaign = Store().CreateCampaign("Concurrent");
        var state = new SystemLab().ExportState();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = TrySave();
        var second = TrySave();
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, saved => saved);
        Assert.Equal(1L, Store().GetCampaign(campaign.Id).HeadSequence);
        Assert.Single(Store().ListCheckpoints(campaign.Id));

        async Task<bool> TrySave()
        {
            await start.Task;
            try
            {
                Store().SaveRecovery(campaign.Id, 0, Build, state);
                return true;
            }
            catch (CheckpointConflictException error)
            {
                Assert.Equal(0, error.ExpectedSequence);
                Assert.Equal(1, error.ActualSequence);
                return false;
            }
        }
    }

    [Theory]
    [InlineData("sha256 = zeroblob(32)")]
    [InlineData("payload = zeroblob(0)")]
    [InlineData("simulation_ticks = simulation_ticks + 1")]
    [InlineData("state_format = 2")]
    [InlineData("created_utc = 'invalid'")]
    public void BadCheckpointIsRejectedAndEarlierRecoveryRemainsLoadable(string change)
    {
        var store = Store();
        var campaign = store.CreateCampaign("Corruption");
        var state = new SystemLab().ExportState();
        var first = store.SaveRecovery(campaign.Id, 0, Build, state);
        var second = store.SaveRecovery(campaign.Id, 1, Build, state);
        Execute(campaign.Id, $"PRAGMA ignore_check_constraints = ON; UPDATE checkpoints SET {change} WHERE sequence = 2;");
        var before = File.ReadAllBytes(DatabasePath(campaign.Id));

        Assert.Throws<InvalidDataException>(() => store.LoadCheckpoint(campaign.Id, second.Id));
        var recovery = store.LoadLatestValidCheckpoint(campaign.Id);
        Assert.Equal(first, recovery.Checkpoint!.Info);
        Assert.Equal(1, recovery.RejectedCount);
        Assert.Equal(before, File.ReadAllBytes(DatabasePath(campaign.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorrectHashCannotBypassJsonAndPhysicsValidation(bool validJson)
    {
        var store = Store();
        var campaign = store.CreateCampaign("Invalid state");
        var state = new SystemLab().ExportState();
        var saved = store.SaveRecovery(campaign.Id, 0, Build, state);
        var payload = validJson
            ? SimulationStateJson.Serialize(state with { LastEventSequence = 99 })
            : Encoding.UTF8.GetBytes("{invalid");
        Execute(campaign.Id, "UPDATE checkpoints SET payload = $payload, sha256 = $hash;",
            ("$payload", payload), ("$hash", SHA256.HashData(payload)));

        Assert.Throws<InvalidDataException>(() => store.LoadCheckpoint(campaign.Id, saved.Id));
        var recovery = store.LoadLatestValidCheckpoint(campaign.Id);
        Assert.Null(recovery.Checkpoint);
        Assert.Equal(1, recovery.RejectedCount);
    }

    [Theory]
    [InlineData("PRAGMA user_version = 999;")]
    [InlineData("PRAGMA application_id = 0;")]
    [InlineData("UPDATE campaign SET campaign_id = '00000000000000000000000000000000';")]
    [InlineData("UPDATE campaign SET head_sequence = 5;")]
    [InlineData("UPDATE campaign SET created_utc = 'invalid';")]
    [InlineData("DELETE FROM schema_migrations WHERE version = 2;")]
    public void IncompatibleOrInvalidCampaignIsReportedWithoutReinitializingIt(string change)
    {
        var store = Store();
        var campaign = store.CreateCampaign("Invalid metadata");
        Execute(campaign.Id, change);
        var before = File.ReadAllBytes(DatabasePath(campaign.Id));
        var entry = Assert.Single(store.ListCampaigns());
        Assert.Equal(campaign.Id, entry.Id);
        Assert.Null(entry.Campaign);
        Assert.NotNull(entry.Error);

        Assert.Throws<InvalidDataException>(() => store.MigrateCampaign(campaign.Id));
        Assert.Throws<InvalidDataException>(() => store.SaveRecovery(campaign.Id, 0, Build, new SystemLab().ExportState()));
        Assert.Equal(before, File.ReadAllBytes(DatabasePath(campaign.Id)));
    }

    [Fact]
    public void DiscoveryIgnoresNoncanonicalFilenamesAndOperationsRejectInvalidInput()
    {
        var store = Store();
        var campaign = store.CreateCampaign("../This is a label, not a path");
        File.WriteAllText(Path.Combine(root, "unrelated.db"), "ignore");
        var missingId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new SqliteCampaignStore("../relative"));
        Assert.Throws<ArgumentException>(() => store.GetCampaign(Guid.Empty));
        Assert.Throws<ArgumentException>(() => store.RestoreBackup(campaign.Id, Guid.Empty, "Restored"));
        Assert.Throws<ArgumentException>(() => store.SaveNamed(campaign.Id, 0, "\n", Build, new SystemLab().ExportState()));
        Assert.Throws<FileNotFoundException>(() => store.GetCampaign(missingId));
        Assert.False(File.Exists(DatabasePath(missingId)));
        Assert.Equal(campaign, Assert.Single(store.ListCampaigns()).Campaign);
        Assert.Equal(2, Directory.GetFiles(root).Length);
    }

    [Fact]
    public void ForwardMigrationPreservesStateAndCanBeRetried()
    {
        var store = Store();
        var campaign = store.CreateCampaign("Legacy schema");
        var state = new SystemLab().ExportState();
        var saved = store.SaveNamed(campaign.Id, 0, "Old save", Build, state);
        Execute(campaign.Id, """
            DROP INDEX recovery_sequence;
            DELETE FROM schema_migrations WHERE version = 2;
            PRAGMA user_version = 1;
            """);
        Assert.Equal(saved, store.LoadCheckpoint(campaign.Id, saved.Id).Info);
        Assert.Equal(1L, Scalar(campaign.Id, "PRAGMA user_version;"));

        store.MigrateCampaign(campaign.Id);
        store.MigrateCampaign(campaign.Id);

        Assert.Equal(2L, Scalar(campaign.Id, "PRAGMA user_version;"));
        Assert.Equal(2L, Scalar(campaign.Id, "SELECT COUNT(*) FROM schema_migrations;"));
        Assert.Equal("recovery_sequence", Scalar(campaign.Id, "SELECT name FROM sqlite_schema WHERE name = 'recovery_sequence';"));
        Assert.Equal(saved, store.LoadCheckpoint(campaign.Id, saved.Id).Info);
        Assert.Equal(SimulationStateJson.Serialize(state), ReadStoredPayload(campaign.Id));
    }

    [Fact]
    public void VerifiedBackupsIncludeCommittedWalAndRecoverWithoutOriginalDatabase()
    {
        var store = Store();
        var campaign = store.CreateCampaign("Backup");
        var lab = new SystemLab();
        var first = store.SaveNamed(campaign.Id, 0, "First", Build, lab.ExportState());
        var earlier = store.CreateBackup(campaign.Id);
        var earlierBytes = File.ReadAllBytes(BackupPath(campaign.Id, earlier.Id));
        CampaignBackup latest;
        CheckpointInfo second;
        using (var pinned = Open(campaign.Id))
        {
            using var transaction = pinned.BeginTransaction(deferred: true);
            using var read = pinned.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT head_sequence FROM campaign;";
            Assert.Equal(1L, read.ExecuteScalar());
            lab.OrderTravel("planet-nereid");
            second = store.SaveRecovery(campaign.Id, 1, Build, lab.ExportState());
            Assert.True(new FileInfo(DatabasePath(campaign.Id) + "-wal").Length > 0);
            latest = store.CreateBackup(campaign.Id);
        }

        Assert.Equal(earlierBytes, File.ReadAllBytes(BackupPath(campaign.Id, earlier.Id)));
        Assert.Equal(2, store.ListBackups(campaign.Id).Count);
        Assert.False(File.Exists(BackupPath(campaign.Id, latest.Id) + "-wal"));
        var corrupted = Encoding.ASCII.GetBytes("not a sqlite database");
        File.WriteAllBytes(DatabasePath(campaign.Id), corrupted);
        Assert.Null(Assert.Single(store.ListCampaigns()).Campaign);

        var recovered = store.RestoreBackup(campaign.Id, latest.Id, "Recovered");
        Assert.NotEqual(campaign.Id, recovered.Id);
        Assert.Equal(second, store.LoadLatestValidCheckpoint(recovered.Id).Checkpoint!.Info);
        Assert.Equal(SimulationStateJson.Serialize(lab.ExportState()),
            SimulationStateJson.Serialize(store.LoadCheckpoint(recovered.Id, second.Id).World.ExportState()));
        var fromEarlier = store.RestoreBackup(campaign.Id, earlier.Id, "Earlier");
        Assert.Equal(first, store.LoadLatestValidCheckpoint(fromEarlier.Id).Checkpoint!.Info);
        Assert.Equal(corrupted, File.ReadAllBytes(DatabasePath(campaign.Id)));
    }

    [Fact]
    public void FailedBackupAndFailedRestoreDoNotPublishOrReplaceFiles()
    {
        var store = Store();
        var campaign = store.CreateCampaign("Backup failure");
        var state = new SystemLab().ExportState();
        store.SaveRecovery(campaign.Id, 0, Build, state);
        var backup = store.CreateBackup(campaign.Id);
        var path = BackupPath(campaign.Id, backup.Id);
        var bytes = File.ReadAllBytes(path);

        Execute(campaign.Id, "UPDATE checkpoints SET sha256 = zeroblob(32);");
        Assert.Throws<InvalidDataException>(() => store.CreateBackup(campaign.Id));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(backup.Id, Assert.Single(store.ListBackups(campaign.Id)).Id);

        File.WriteAllText(path, "damaged backup");
        Assert.Throws<SqliteException>(() => store.RestoreBackup(campaign.Id, backup.Id, "Restored"));
        Assert.Single(store.ListCampaigns());
        Assert.Empty(Directory.GetFiles(root, ".pending-*", SearchOption.AllDirectories));
    }

    private string DatabasePath(Guid campaignId) => Path.Combine(root, $"{campaignId:N}.db");
    private string BackupPath(Guid campaignId, Guid backupId) => Path.Combine(root, "backups", $"{campaignId:N}-{backupId:N}.db");
    private byte[] ReadStoredPayload(Guid campaignId) => (byte[])Scalar(campaignId, "SELECT payload FROM checkpoints;")!;

    private SqliteConnection Open(Guid campaignId)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(campaignId),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private object? Scalar(Guid campaignId, string sql)
    {
        using var connection = Open(campaignId);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Execute(Guid campaignId, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(campaignId);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
