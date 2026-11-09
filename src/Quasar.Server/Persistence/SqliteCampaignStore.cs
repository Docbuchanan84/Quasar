using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Quasar.Simulation;

namespace Quasar.Server.Persistence;

public sealed partial class SqliteCampaignStore : ICampaignStore
{
    public const int DefaultRecoveryRetention = 5;
    private const int MaxPayloadBytes = 64 * 1024 * 1024;
    private const int MaxLabelLength = 120;
    private const int MaxBuildLength = 128;
    private const string CheckpointColumns = """
        save_id, sequence, kind, label, created_utc, game_build, simulation_ticks, state_format,
        CASE WHEN typeof(sha256) = 'blob' AND length(sha256) = 32 THEN sha256 END
        """;

    private readonly string root;
    private readonly int recoveryRetention;

    // The root is trusted host configuration. Public operations accept IDs only.
    public SqliteCampaignStore(string dataRoot, int recoveryRetention = DefaultRecoveryRetention)
    {
        if (!Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("Campaign storage requires an absolute data directory.", nameof(dataRoot));
        ArgumentOutOfRangeException.ThrowIfLessThan(recoveryRetention, 1);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        this.recoveryRetention = recoveryRetention;
    }

    public static SqliteCampaignStore ForHostedDesktop(int recoveryRetention = DefaultRecoveryRetention)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SqliteCampaignStore(Path.Combine(localData, "Quasar", "saves"), recoveryRetention);
    }

    public CampaignInfo CreateCampaign(string displayName)
    {
        RequireInputText(displayName, MaxLabelLength, nameof(displayName));
        var now = DateTimeOffset.UtcNow;
        var campaign = new CampaignInfo(Guid.NewGuid(), displayName, now, now, 0);
        PublishDatabase(CampaignPath(campaign.Id), path =>
        {
            using var connection = Open(path, SqliteOpenMode.ReadWrite);
            using var transaction = connection.BeginTransaction();
            InitializeSchema(connection, transaction);
            using var insert = Command(connection, transaction, """
                INSERT INTO campaign(singleton, campaign_id, display_name, created_utc, updated_utc, head_sequence)
                VALUES (1, $id, $name, $now, $now, 0);
                """, ("$id", IdText(campaign.Id)), ("$name", displayName), ("$now", UtcText(now)));
            insert.ExecuteNonQuery();
            transaction.Commit();
        });
        return campaign;
    }

    public IReadOnlyList<CampaignDiscovery> ListCampaigns()
    {
        RequirePlainPath(root);
        if (!Directory.Exists(root))
            return [];

        var campaigns = new List<CampaignDiscovery>();
        foreach (var path in Directory.EnumerateFiles(root, "*.db"))
        {
            if (!TryReadId(Path.GetFileNameWithoutExtension(path), out var id))
                continue;
            try
            {
                campaigns.Add(new(id, GetCampaign(id), null));
            }
            catch (Exception error) when (error is InvalidDataException or SqliteException or IOException or UnauthorizedAccessException)
            {
                campaigns.Add(new(id, null, error.Message));
            }
        }
        return campaigns.OrderByDescending(entry => entry.Campaign?.UpdatedUtc).ThenBy(entry => entry.Id).ToArray();
    }

    public CampaignInfo GetCampaign(Guid campaignId)
    {
        using var connection = Open(CampaignPath(campaignId), SqliteOpenMode.ReadOnly);
        using var transaction = connection.BeginTransaction(deferred: true);
        return ReadCampaign(connection, transaction, campaignId);
    }

    public void MigrateCampaign(Guid campaignId)
    {
        var path = CampaignPath(campaignId);
        GetCampaign(campaignId); // Reject unknown or invalid files before opening a writer.
        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using var transaction = connection.BeginTransaction();
        ReadCampaign(connection, transaction, campaignId);
        ApplyMigrations(connection, transaction, ReadSchemaVersion(connection, transaction));
        transaction.Commit();
    }

    public IReadOnlyList<CheckpointInfo> ListCheckpoints(Guid campaignId)
    {
        using var connection = Open(CampaignPath(campaignId), SqliteOpenMode.ReadOnly);
        using var transaction = connection.BeginTransaction(deferred: true);
        var campaign = ReadCampaign(connection, transaction, campaignId);
        using var command = Command(connection, transaction,
            $"SELECT {CheckpointColumns} FROM checkpoints ORDER BY sequence DESC;");
        using var reader = command.ExecuteReader();
        var checkpoints = new List<CheckpointInfo>();
        while (reader.Read())
            checkpoints.Add(ReadCheckpointInfo(reader, campaign));
        return checkpoints;
    }

    public CheckpointInfo SaveNamed(Guid campaignId, long expectedHead, string label, string gameBuild, SystemLabState state)
    {
        RequireInputText(label, MaxLabelLength, nameof(label));
        return Save(campaignId, expectedHead, CheckpointKind.Named, label, gameBuild, state);
    }

    public CheckpointInfo SaveRecovery(Guid campaignId, long expectedHead, string gameBuild, SystemLabState state) =>
        Save(campaignId, expectedHead, CheckpointKind.Recovery, null, gameBuild, state);

    private CheckpointInfo Save(Guid campaignId, long expectedHead, CheckpointKind kind, string? label,
        string gameBuild, SystemLabState state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedHead);
        RequireInputText(gameBuild, MaxBuildLength, nameof(gameBuild));
        var path = CampaignPath(campaignId);
        RequireHead(GetCampaign(campaignId), expectedHead);
        SystemLab.RestoreState(state);
        var payload = SimulationStateJson.Serialize(state);
        if (payload.Length > MaxPayloadBytes)
            throw new InvalidDataException("Checkpoint payload exceeds the storage limit.");

        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using (var wal = Command(connection, null, "PRAGMA journal_mode = WAL;"))
        {
            if (wal.ExecuteScalar() is not "wal")
                throw new IOException("Could not enable WAL for the campaign.");
        }

        // BEGIN IMMEDIATE serializes writers before the head is checked again.
        using var transaction = connection.BeginTransaction();
        var campaign = ReadCampaign(connection, transaction, campaignId);
        RequireHead(campaign, expectedHead);
        ApplyMigrations(connection, transaction, ReadSchemaVersion(connection, transaction));

        var now = DateTimeOffset.UtcNow;
        if (now < campaign.UpdatedUtc)
            now = campaign.UpdatedUtc;
        var sequence = checked(expectedHead + 1);
        var id = Guid.NewGuid();
        var digest = SHA256.HashData(payload);
        var info = new CheckpointInfo(id, sequence, kind, label, now, gameBuild,
            state.Simulation.ElapsedTicks, state.Simulation.FormatVersion, Convert.ToHexString(digest));

        using (var insert = Command(connection, transaction, """
            INSERT INTO checkpoints(save_id, sequence, kind, label, created_utc, game_build,
                simulation_ticks, state_format, sha256, payload)
            VALUES ($id, $sequence, $kind, $label, $now, $build, $ticks, $version, $digest, $payload);
            """, ("$id", IdText(id)), ("$sequence", sequence), ("$kind", (int)kind), ("$label", label),
            ("$now", UtcText(now)), ("$build", gameBuild), ("$ticks", info.SimulationTicks),
            ("$version", info.StateFormatVersion), ("$digest", digest), ("$payload", payload)))
            insert.ExecuteNonQuery();

        using (var update = Command(connection, transaction, """
            UPDATE campaign SET updated_utc = $now, head_sequence = $sequence WHERE singleton = 1;
            """, ("$now", UtcText(now)), ("$sequence", sequence)))
            update.ExecuteNonQuery();

        if (kind == CheckpointKind.Recovery)
        {
            using var rotate = Command(connection, transaction, """
                DELETE FROM checkpoints WHERE kind = 2 AND sequence IN (
                    SELECT sequence FROM checkpoints WHERE kind = 2 ORDER BY sequence DESC LIMIT -1 OFFSET $retain
                );
                """, ("$retain", recoveryRetention));
            rotate.ExecuteNonQuery();
        }

        transaction.Commit();
        return info;
    }

    public LoadedCheckpoint LoadCheckpoint(Guid campaignId, Guid checkpointId)
    {
        using var connection = Open(CampaignPath(campaignId), SqliteOpenMode.ReadOnly);
        using var transaction = connection.BeginTransaction(deferred: true);
        var campaign = ReadCampaign(connection, transaction, campaignId);
        using var command = ReadPayloads(connection, transaction, "WHERE save_id = $id", ("$id", IdText(checkpointId)));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCheckpoint(reader, campaign)
            : throw new KeyNotFoundException("Checkpoint was not found in the campaign.");
    }

    public CheckpointRecovery LoadLatestValidCheckpoint(Guid campaignId)
    {
        using var connection = Open(CampaignPath(campaignId), SqliteOpenMode.ReadOnly);
        using var transaction = connection.BeginTransaction(deferred: true);
        var campaign = ReadCampaign(connection, transaction, campaignId);
        using var command = ReadPayloads(connection, transaction, "ORDER BY sequence DESC");
        using var reader = command.ExecuteReader();
        var rejected = 0;
        while (reader.Read())
        {
            try
            {
                return new(ReadCheckpoint(reader, campaign), rejected);
            }
            catch (InvalidDataException)
            {
                rejected++;
            }
        }
        return new(null, rejected);
    }

    private static SqliteCommand ReadPayloads(SqliteConnection connection, SqliteTransaction transaction,
        string clause, params (string Name, object? Value)[] parameters)
    {
        // Bound allocation before fetching blobs from files we did not create.
        return Command(connection, transaction, $"""
            SELECT {CheckpointColumns},
                CASE WHEN typeof(payload) = 'blob' AND length(payload) BETWEEN 1 AND $max THEN payload END
            FROM checkpoints {clause};
            """, [("$max", MaxPayloadBytes), .. parameters]);
    }

    private static LoadedCheckpoint ReadCheckpoint(SqliteDataReader reader, CampaignInfo campaign)
    {
        var info = ReadCheckpointInfo(reader, campaign);
        if (info.StateFormatVersion != SimulationState.CurrentFormatVersion)
            throw new InvalidDataException("Unsupported checkpoint state format.");
        if (reader.GetValue(9) is not byte[] payload
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), Convert.FromHexString(info.Sha256)))
            throw new InvalidDataException("Invalid checkpoint payload size, type, or SHA-256.");

        var state = SimulationStateJson.Deserialize(payload);
        if (state.Simulation.FormatVersion != info.StateFormatVersion || state.Simulation.ElapsedTicks != info.SimulationTicks)
            throw new InvalidDataException("Checkpoint metadata disagrees with the payload.");
        return new(info, state, SystemLab.RestoreState(state));
    }

    private static CheckpointInfo ReadCheckpointInfo(SqliteDataReader reader, CampaignInfo campaign)
    {
        var id = ReadId(reader, 0);
        var sequence = ReadInteger(reader, 1);
        var kind = ReadInteger(reader, 2);
        var label = reader.IsDBNull(3) ? null : ReadText(reader, 3, MaxLabelLength);
        var created = ReadUtc(reader, 4);
        var build = ReadText(reader, 5, MaxBuildLength);
        var ticks = ReadInteger(reader, 6);
        var format = ReadInteger(reader, 7);
        if (sequence < 1 || sequence > campaign.HeadSequence
            || kind is not ((int)CheckpointKind.Named or (int)CheckpointKind.Recovery)
            || (kind == (int)CheckpointKind.Named) != (label is not null)
            || created < campaign.CreatedUtc || created > campaign.UpdatedUtc
            || format is < 1 or > int.MaxValue || reader.GetValue(8) is not byte[] { Length: 32 } digest)
            throw new InvalidDataException("Invalid checkpoint metadata.");
        return new(id, sequence, (CheckpointKind)kind, label, created, build, ticks, (int)format, Convert.ToHexString(digest));
    }

    private static void RequireHead(CampaignInfo campaign, long expectedHead)
    {
        if (campaign.HeadSequence != expectedHead)
            throw new CheckpointConflictException(expectedHead, campaign.HeadSequence);
    }
}
