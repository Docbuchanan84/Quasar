using Microsoft.Data.Sqlite;

namespace Quasar.Server.Persistence;

public sealed partial class SqliteCampaignStore
{
    public CampaignBackup CreateBackup(Guid campaignId)
    {
        var source = CampaignPath(campaignId);
        GetCampaign(campaignId);
        var backupId = Guid.NewGuid();
        var destination = BackupPath(campaignId, backupId);
        PublishDatabase(destination, temporary =>
        {
            CopyDatabase(source, temporary);
            VerifyBackup(temporary, campaignId);
        });
        return new(backupId, File.GetLastWriteTimeUtc(destination));
    }

    public IReadOnlyList<CampaignBackup> ListBackups(Guid campaignId)
    {
        var prefix = IdText(campaignId) + "-";
        var directory = BackupDirectory();
        if (!Directory.Exists(directory))
            return [];

        var backups = new List<CampaignBackup>();
        foreach (var path in Directory.EnumerateFiles(directory, prefix + "*.db"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.StartsWith(prefix, StringComparison.Ordinal)
                && TryReadId(stem[prefix.Length..], out var backupId))
                backups.Add(new(backupId, File.GetLastWriteTimeUtc(path)));
        }
        return backups.OrderByDescending(backup => backup.LastWriteUtc).ThenBy(backup => backup.Id).ToArray();
    }

    public CampaignInfo RestoreBackup(Guid campaignId, Guid backupId, string displayName)
    {
        RequireInputText(displayName, MaxLabelLength, nameof(displayName));
        var source = BackupPath(campaignId, backupId);
        var restoredId = Guid.NewGuid();
        var destination = CampaignPath(restoredId);
        PublishDatabase(destination, temporary =>
        {
            RequireStandaloneBackup(source);
            CopyDatabase(source, temporary);
            using (var connection = Open(temporary, SqliteOpenMode.ReadWrite))
            using (var transaction = connection.BeginTransaction())
            {
                ReadCampaign(connection, transaction, campaignId);
                ApplyMigrations(connection, transaction, ReadSchemaVersion(connection, transaction));
                using var update = Command(connection, transaction, """
                    UPDATE campaign SET campaign_id = $id, display_name = $name WHERE singleton = 1;
                    """, ("$id", IdText(restoredId)), ("$name", displayName));
                update.ExecuteNonQuery();
                transaction.Commit();
            }
            VerifyBackup(temporary, restoredId);
        });
        return GetCampaign(restoredId);
    }

    private string BackupDirectory()
    {
        RequirePlainPath(root);
        var directory = Path.Combine(root, "backups");
        RequirePlainPath(directory);
        return directory;
    }

    private string BackupPath(Guid campaignId, Guid backupId) =>
        Path.Combine(BackupDirectory(), $"{IdText(campaignId)}-{IdText(backupId)}.db");

    private static void CopyDatabase(string source, string destination)
    {
        using var from = Open(source, SqliteOpenMode.ReadOnly);
        using var to = Open(destination, SqliteOpenMode.ReadWrite);
        from.BackupDatabase(to);
        using var journal = Command(to, null, "PRAGMA journal_mode = DELETE;");
        if (journal.ExecuteScalar() is not "delete")
            throw new IOException("Could not make the backup independent of WAL files.");
    }

    private static void VerifyBackup(string path, Guid campaignId)
    {
        RequireStandaloneBackup(path);
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var integrity = Command(connection, transaction, "PRAGMA integrity_check(1);");
        if (integrity.ExecuteScalar() is not "ok")
            throw new InvalidDataException("SQLite backup failed its integrity check.");

        var campaign = ReadCampaign(connection, transaction, campaignId);
        if (campaign.HeadSequence == 0)
            throw new InvalidDataException("A campaign needs a checkpoint before it can be backed up.");

        using var command = ReadPayloads(connection, transaction, "ORDER BY sequence");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            ReadCheckpoint(reader, campaign);
    }

    private static void RequireStandaloneBackup(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            RequirePlainPath(path + suffix);
            if (File.Exists(path + suffix))
                throw new InvalidDataException("A backup must be a standalone SQLite database.");
        }
    }
}
