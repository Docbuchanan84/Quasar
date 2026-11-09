using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Quasar.Server.Persistence;

public sealed partial class SqliteCampaignStore
{
    private const int ApplicationId = 0x51534152; // QSAR
    private const int CurrentSchemaVersion = 2;

    private static void InitializeSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var setup = Command(connection, transaction, $"""
            PRAGMA application_id = {ApplicationId};
            CREATE TABLE schema_migrations (
                version INTEGER PRIMARY KEY CHECK (version > 0),
                applied_utc TEXT NOT NULL
            ) STRICT;
            """);
        setup.ExecuteNonQuery();
        ApplyMigrations(connection, transaction, 0);
    }

    private static void ApplyMigrations(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        while (version < CurrentSchemaVersion)
        {
            version++;
            var sql = version switch
            {
                1 => """
                    CREATE TABLE campaign (
                        singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                        campaign_id TEXT NOT NULL CHECK (length(campaign_id) = 32),
                        display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 1 AND 120),
                        created_utc TEXT NOT NULL,
                        updated_utc TEXT NOT NULL,
                        head_sequence INTEGER NOT NULL CHECK (head_sequence >= 0)
                    ) STRICT;
                    CREATE TABLE checkpoints (
                        sequence INTEGER PRIMARY KEY CHECK (sequence > 0),
                        save_id TEXT NOT NULL UNIQUE CHECK (length(save_id) = 32),
                        kind INTEGER NOT NULL CHECK (kind IN (1, 2)),
                        label TEXT,
                        created_utc TEXT NOT NULL,
                        game_build TEXT NOT NULL CHECK (length(game_build) BETWEEN 1 AND 128),
                        simulation_ticks INTEGER NOT NULL,
                        state_format INTEGER NOT NULL CHECK (state_format > 0),
                        sha256 BLOB NOT NULL CHECK (length(sha256) = 32),
                        payload BLOB NOT NULL CHECK (length(payload) BETWEEN 1 AND 67108864),
                        CHECK ((kind = 1 AND label IS NOT NULL AND length(label) BETWEEN 1 AND 120)
                            OR (kind = 2 AND label IS NULL))
                    ) STRICT;
                    """,
                2 => "CREATE INDEX recovery_sequence ON checkpoints(sequence DESC) WHERE kind = 2;",
                _ => throw new InvalidOperationException("Missing SQLite migration.")
            };
            using var migration = Command(connection, transaction, $"""
                {sql}
                INSERT INTO schema_migrations(version, applied_utc) VALUES ($version, $now);
                PRAGMA user_version = {version};
                """, ("$version", version), ("$now", UtcText(DateTimeOffset.UtcNow)));
            migration.ExecuteNonQuery();
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var appId = Command(connection, transaction, "PRAGMA application_id;");
        if (appId.ExecuteScalar() is not long id || id != ApplicationId)
            throw new InvalidDataException("File is not a Quasar campaign database.");

        using var schemaVersion = Command(connection, transaction, "PRAGMA user_version;");
        if (schemaVersion.ExecuteScalar() is not long version || version < 1)
            throw new InvalidDataException("Invalid Quasar campaign schema version.");
        if (version > CurrentSchemaVersion)
            throw new InvalidDataException($"Campaign schema {version} is newer than supported schema {CurrentSchemaVersion}.");

        using var ledger = Command(connection, transaction,
            "SELECT version, applied_utc FROM schema_migrations ORDER BY version LIMIT $limit;", ("$limit", version + 1));
        using var reader = ledger.ExecuteReader();
        for (var expected = 1; expected <= version; expected++)
        {
            if (!reader.Read() || ReadInteger(reader, 0) != expected)
                throw new InvalidDataException("Invalid campaign migration history.");
            ReadUtc(reader, 1);
        }
        if (reader.Read())
            throw new InvalidDataException("Unexpected campaign migration history.");
        return (int)version;
    }

    private static CampaignInfo ReadCampaign(SqliteConnection connection, SqliteTransaction transaction, Guid expectedId)
    {
        ReadSchemaVersion(connection, transaction);
        using var command = Command(connection, transaction, """
            SELECT singleton, campaign_id, display_name, created_utc, updated_utc, head_sequence,
                (SELECT COALESCE(MAX(sequence), 0) FROM checkpoints)
            FROM campaign LIMIT 2;
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || ReadInteger(reader, 0) != 1 || ReadId(reader, 1) != expectedId)
            throw new InvalidDataException("Campaign metadata does not match its file identity.");

        var name = ReadText(reader, 2, MaxLabelLength);
        var created = ReadUtc(reader, 3);
        var updated = ReadUtc(reader, 4);
        var head = ReadInteger(reader, 5);
        if (created > updated || head < 0 || head != ReadInteger(reader, 6) || reader.Read())
            throw new InvalidDataException("Invalid campaign metadata.");
        return new(expectedId, name, created, updated, head);
    }

    private string CampaignPath(Guid id) => Path.Combine(root, IdText(id) + ".db");

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        RequirePlainPath(Path.GetDirectoryName(path)!);
        RequirePlainPath(path);
        RequirePlainPath(path + "-wal");
        RequirePlainPath(path + "-shm");
        RequirePlainPath(path + "-journal");
        if (!File.Exists(path))
            throw new FileNotFoundException("Campaign database was not found.", path);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            using var settings = Command(connection, null, "PRAGMA trusted_schema = OFF; PRAGMA synchronous = FULL;");
            settings.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void RequirePlainPath(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Campaign files and storage directories cannot be links.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void PublishDatabase(string destination, Action<string> prepare)
    {
        var directory = Path.GetDirectoryName(destination)!;
        RequirePlainPath(directory);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".pending-{Guid.NewGuid():N}.db");
        using (File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            prepare(temporary);
            File.Move(temporary, destination); // Same directory, atomic publication, no overwrite.
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string IdText(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Storage IDs must be non-empty.", nameof(id));
        return id.ToString("N");
    }

    private static bool TryReadId(string value, out Guid id) =>
        Guid.TryParseExact(value, "N", out id) && id != Guid.Empty && value == id.ToString("N");

    private static Guid ReadId(SqliteDataReader reader, int ordinal) =>
        TryReadId(ReadText(reader, ordinal, 32), out var id) ? id : throw new InvalidDataException("Invalid storage ID.");

    private static long ReadInteger(SqliteDataReader reader, int ordinal) =>
        reader.GetValue(ordinal) is long value ? value : throw new InvalidDataException("Expected an SQLite integer.");

    private static string UtcText(DateTimeOffset date) => date.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadUtc(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.TryParseExact(ReadText(reader, ordinal, 33), "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) && date.Offset == TimeSpan.Zero
                ? date : throw new InvalidDataException("Invalid UTC storage timestamp.");

    private static string ReadText(SqliteDataReader reader, int ordinal, int maxLength) =>
        reader.GetValue(ordinal) is string value && ValidText(value, maxLength)
            ? value : throw new InvalidDataException("Invalid storage text metadata.");

    private static bool ValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private static void RequireInputText(string value, int maxLength, string parameter)
    {
        if (!ValidText(value, maxLength))
            throw new ArgumentException($"Expected nonblank text of at most {maxLength} characters without control characters.", parameter);
    }
}
