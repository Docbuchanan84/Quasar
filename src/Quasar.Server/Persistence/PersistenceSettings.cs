using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quasar.Server.Persistence;

public sealed record AutosaveSettings
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 300;
    public int RecoveryCheckpoints { get; init; } = SqliteCampaignStore.DefaultRecoveryRetention;
    public bool SaveOnStop { get; init; }
    public bool SaveOnSignificantEvent { get; init; }
}

public sealed record PersistenceSettings
{
    public AutosaveSettings Autosave { get; init; } = new();
    public bool SaveOnShutdown { get; init; } = true;

    public static PersistenceSettings Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var settings = JsonSerializer.Deserialize<PersistenceSettings>(stream, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                AllowDuplicateProperties = false
            });
            if (settings?.Autosave is null || settings.Autosave.IntervalSeconds is < 1 or > 86400
                || settings.Autosave.RecoveryCheckpoints is < 1 or > 100)
                throw new InvalidDataException("Expected autosave intervalSeconds of 1–86400 and recoveryCheckpoints of 1–100.");
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException error)
        {
            throw new InvalidDataException("Invalid persistence settings JSON. Correct the file before starting Quasar.", error);
        }
    }
}

internal sealed record PersistencePaths(string DataRoot, string SettingsFile)
{
    public static PersistencePaths Resolve(IHostEnvironment environment, IConfiguration configuration)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var quasar = Path.Combine(local, "Quasar");
        var dataRoot = configuration["Persistence:DataRoot"] ?? Path.Combine(quasar,
            environment.IsDevelopment() ? "dev-saves" : "saves");
        var settingsFile = configuration["Persistence:SettingsFile"] ?? Path.Combine(quasar, "config", "persistence.json");
        return new(Absolute(dataRoot), Absolute(settingsFile));
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.GetFullPath(path)
        : throw new InvalidDataException("Persistence paths must be absolute host paths.");
}
