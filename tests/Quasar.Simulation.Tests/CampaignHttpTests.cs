using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quasar.Server;
using Quasar.Server.Persistence;

namespace Quasar.Simulation.Tests;

public sealed class CampaignHttpTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("quasar-http-").FullName;
    private const string Status = "/api/campaigns/status";
    private const string Lab = "/api/lab";

    [Theory]
    [InlineData("midflight")]
    [InlineData("arrived")]
    [InlineData("cancelled")]
    [InlineData("moon")]
    public async Task RestartAndContinueRestoreExactMovementAndHistory(string scenario)
    {
        Guid campaignId;
        string instanceId;
        JsonElement expectedWorld;
        JsonElement expectedShipState;
        long expectedTicks;

        await using (var host = await Start())
        {
            var created = await host.Create("Pathfinder");
            campaignId = created.GetProperty("campaign").GetProperty("id").GetGuid();
            instanceId = created.GetProperty("instanceId").GetString()!;
            var preview = scenario == "moon"
                ? (await host.Get(Lab + "/course-preview?mode=auto&destinationId=moon-moon")).GetProperty("fastest")
                : await host.Get(Lab + "/course-preview?destinationId=planet-earth");
            await host.Command("commit-course", previewId: preview.GetProperty("previewId").GetString());
            if (scenario == "arrived")
            {
                await host.Advance(1296000);
            }
            else
            {
                await host.Advance(3600);
                if (scenario == "cancelled")
                {
                    await host.Command("cancel-travel");
                    await host.Advance(30);
                }
            }

            var before = await host.Get(Lab);
            expectedWorld = before.GetProperty("world");
            expectedTicks = long.Parse(expectedWorld.GetProperty("elapsedTicks").GetString()!);
            expectedShipState = await host.Get(Lab + "/spacecraft/state?atTicks=" + expectedTicks);
            var save = await host.Post("/api/campaigns/save-named", before, new { label = scenario }, HttpStatusCode.Created);
            Assert.Equal("named", save.GetProperty("kind").GetString());
            Assert.Equal(expectedTicks.ToString(), save.GetProperty("simulationTicks").GetString());
            Assert.Equal("2", save.GetProperty("sequence").GetString());
        }

        await using var restarted = await Start();
        var restored = await restarted.Get(Status);
        Assert.NotEqual(instanceId, restored.GetProperty("instanceId").GetString());
        Assert.False(restored.GetProperty("running").GetBoolean());
        Assert.Equal(expectedWorld.GetRawText(), (await restarted.Get(Lab)).GetProperty("world").GetRawText());
        Assert.Equal(expectedShipState.GetRawText(),
            (await restarted.Get(Lab + "/spacecraft/state?atTicks=" + expectedTicks)).GetRawText());

        var continued = await restarted.Post("/api/campaigns/continue", restored, new { campaignId });
        Assert.False(continued.GetProperty("running").GetBoolean());
        Assert.NotEqual(restored.GetProperty("instanceId").GetString(), continued.GetProperty("instanceId").GetString());
        if (scenario == "midflight")
        {
            var arrived = await restarted.Advance(1296000);
            var world = arrived.GetProperty("world");
            Assert.Equal(expectedWorld.GetProperty("trajectory").GetProperty("arrivalTicks").GetString(),
                world.GetProperty("elapsedTicks").GetString());
            Assert.Single(world.GetProperty("events").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "arrival");
        }
    }

    [Fact]
    public async Task ContinuousRunsWithoutBrowserRequestsAndAnotherClientCanStopIt()
    {
        await using var host = await Start();
        await host.Create("Unattended run");
        await host.Command("configure", duration: 1, mode: "continuous");
        var play = await host.Command("play");

        // Exceed the former five-second browser lease with no requests at all.
        await Task.Delay(TimeSpan.FromSeconds(6));
        var reconnected = await host.Get(Lab);
        Assert.True(reconnected.GetProperty("running").GetBoolean());
        Assert.NotEqual(play.GetProperty("world").GetProperty("elapsedTicks").GetString(),
            reconnected.GetProperty("world").GetProperty("elapsedTicks").GetString());

        var stopped = await host.Post(Lab + "/commands", play, new { clientId = Guid.NewGuid(), operation = "stop" });
        Assert.False(stopped.GetProperty("running").GetBoolean());
        Assert.Equal("Player stopped", stopped.GetProperty("stopReason").GetString());
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("interrupt").ValueKind);
        await Task.Delay(TimeSpan.FromMilliseconds(350));
        Assert.Equal(stopped.GetProperty("world").GetProperty("elapsedTicks").GetString(),
            (await host.Get(Lab)).GetProperty("world").GetProperty("elapsedTicks").GetString());
    }

    [Fact]
    public async Task LoadingWhileRunningPausesAndRejectsOldCommandsAndPreviews()
    {
        await using var host = await Start();
        var first = await host.Create("First campaign");
        var second = await host.Create("Second campaign");
        var campaignId = first.GetProperty("campaign").GetProperty("id").GetGuid();
        var checkpointId = first.GetProperty("lastCheckpoint").GetProperty("id").GetGuid();
        var preview = await host.Get(Lab + "/course-preview?destinationId=planet-earth");
        await host.Command("configure", duration: 1, mode: "continuous");
        await host.Command("play");

        var until = DateTime.UtcNow.AddSeconds(3);
        JsonElement loaded;
        while (true)
        {
            var running = await host.Get(Lab);
            Assert.True(running.GetProperty("running").GetBoolean());
            using var response = await host.Send("/api/campaigns/load", running, new { campaignId, checkpointId });
            if (response.StatusCode == HttpStatusCode.OK)
            {
                loaded = await Host.Read(response, HttpStatusCode.OK);
                break;
            }

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.True(DateTime.UtcNow < until, "Could not submit a current revision while running.");
        }
        Assert.False(loaded.GetProperty("running").GetBoolean());
        Assert.NotEqual(second.GetProperty("instanceId").GetString(), loaded.GetProperty("instanceId").GetString());
        await host.Post(Lab + "/commands", second, new { clientId = Guid.NewGuid(), operation = "stop" }, HttpStatusCode.Conflict);
        await host.Post(Lab + "/commands", loaded, new
        {
            clientId = Guid.NewGuid(),
            operation = "commit-course",
            previewId = preview.GetProperty("previewId").GetString()
        }, HttpStatusCode.Conflict);
        var campaigns = await host.Get("/api/campaigns");
        Assert.Equal(2, campaigns.GetArrayLength());
        Assert.Equal("0", (await host.Get(Lab)).GetProperty("world").GetProperty("elapsedTicks").GetString());
    }

    [Fact]
    public async Task CorruptHeadFallsBackAndAnExplicitFailedLoadPreservesTheCurrentWorld()
    {
        var store = new SqliteCampaignStore(root);
        Guid campaignId;
        CheckpointInfo corrupt;
        CheckpointInfo prior;
        await using (var host = await Start())
        {
            var created = await host.Create("Recovery");
            campaignId = created.GetProperty("campaign").GetProperty("id").GetGuid();
            await host.Advance(1);
            await host.Post("/api/campaigns/save", await host.Get(Status), new { }, HttpStatusCode.Created);
            prior = store.LoadLatestValidCheckpoint(campaignId).Checkpoint!.Info;
            await host.Advance(5);
            await host.Post("/api/campaigns/save", await host.Get(Status), new { }, HttpStatusCode.Created);
            corrupt = store.LoadLatestValidCheckpoint(campaignId).Checkpoint!.Info;
        }

        Sql(campaignId, $"UPDATE checkpoints SET payload = X'00' WHERE sequence = {corrupt.Sequence};");
        await using var restarted = await Start();
        var status = await restarted.Get(Status);
        Assert.Equal(1, status.GetProperty("rejectedCheckpoints").GetInt32());
        Assert.Equal(prior.Id, status.GetProperty("lastCheckpoint").GetProperty("id").GetGuid());
        Assert.Equal(corrupt.Sequence.ToString(), status.GetProperty("campaign").GetProperty("headSequence").GetString());

        var failure = await restarted.Post("/api/campaigns/load", status, new { campaignId, checkpointId = corrupt.Id },
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal("invalid_campaign_data", failure.GetProperty("code").GetString());
        Assert.Equal(status.GetRawText(), (await restarted.Get(Status)).GetRawText());
        var list = await restarted.Get($"/api/campaigns/{campaignId}/checkpoints");
        Assert.Contains(list.GetProperty("checkpoints").EnumerateArray(), item => item.GetProperty("id").GetGuid() == prior.Id);

        await restarted.Post("/api/campaigns/save", status, new { }, HttpStatusCode.Created);
        Assert.Equal(corrupt.Sequence + 1, store.LoadLatestValidCheckpoint(campaignId).Checkpoint!.Info.Sequence);
    }

    [Fact]
    public async Task StartupUsesVerifiedBackupWithoutReplacingBrokenDatabase()
    {
        Guid campaignId;
        var store = new SqliteCampaignStore(root);
        await using (var host = await Start())
        {
            var created = await host.Create("Backed up");
            campaignId = created.GetProperty("campaign").GetProperty("id").GetGuid();
            await host.Advance(5);
            await host.Post("/api/campaigns/save", await host.Get(Status), new { }, HttpStatusCode.Created);
            store.CreateBackup(campaignId);
        }

        var badBytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Database(campaignId), badBytes);

        await using var restarted = await Start();
        var recovered = await restarted.Get(Status);
        Assert.Equal(campaignId, recovered.GetProperty("recoveredFromCampaignId").GetGuid());
        Assert.NotEqual(campaignId, recovered.GetProperty("campaign").GetProperty("id").GetGuid());
        Assert.Equal("50000000", recovered.GetProperty("lastCheckpoint").GetProperty("simulationTicks").GetString());
        Assert.Equal(badBytes, File.ReadAllBytes(Database(campaignId)));
    }

    [Fact]
    public async Task UnrecoverableStartupIsDifferentFromFirstRun()
    {
        Guid id;
        await using (var host = await Start())
        {
            var empty = await host.Get(Status);
            Assert.Equal(JsonValueKind.Null, empty.GetProperty("campaign").ValueKind);
            Assert.Equal(JsonValueKind.Null, empty.GetProperty("recoveryError").ValueKind);
            Assert.Equal("no_active_campaign", (await host.Get(Lab, HttpStatusCode.Conflict)).GetProperty("code").GetString());
            id = (await host.Create("Unrecoverable")).GetProperty("campaign").GetProperty("id").GetGuid();
        }

        Sql(id, "UPDATE checkpoints SET payload = X'00';");
        var before = File.ReadAllBytes(Database(id));
        await using var restarted = await Start();
        var broken = await restarted.Get(Status);
        Assert.Equal(JsonValueKind.Null, broken.GetProperty("campaign").ValueKind);
        Assert.Equal(JsonValueKind.String, broken.GetProperty("recoveryError").ValueKind);
        Assert.Equal("recovery_failed",
            (await restarted.Get(Lab, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString());
        Assert.Equal(before, File.ReadAllBytes(Database(id)));
        Assert.Single(Directory.GetFiles(root, "*.db"));
    }

    [Fact]
    public async Task StandaloneCampaignCopyOpensInAnotherRootAndFutureSchemaRemainsIntact()
    {
        Guid id;
        JsonElement world;
        await using (var host = await Start())
        {
            var created = await host.Create("Portable");
            id = created.GetProperty("campaign").GetProperty("id").GetGuid();
            await host.Advance(30);
            var before = await host.Get(Lab);
            world = before.GetProperty("world");
            await host.Post("/api/campaigns/save-named", before, new { label = "Portable checkpoint" },
                HttpStatusCode.Created);
        }

        var store = new SqliteCampaignStore(root);
        var backup = store.CreateBackup(id);
        var copyRoot = Directory.CreateDirectory(Path.Combine(root, "portable")).FullName;
        var copiedDatabase = Database(id, copyRoot);
        File.Copy(Path.Combine(root, "backups", $"{id:N}-{backup.Id:N}.db"), copiedDatabase);
        await using (var copied = await Start(dataRoot: copyRoot))
        {
            var status = await copied.Get(Status);
            Assert.Equal(id, status.GetProperty("campaign").GetProperty("id").GetGuid());
            Assert.False(status.GetProperty("running").GetBoolean());
            Assert.Equal("Portable checkpoint", status.GetProperty("lastCheckpoint").GetProperty("label").GetString());
            Assert.Equal(world.GetRawText(), (await copied.Get(Lab)).GetProperty("world").GetRawText());
        }

        Sql(id, "PRAGMA user_version = 999;", copyRoot);
        var beforeBytes = File.ReadAllBytes(copiedDatabase);
        await using (var incompatible = await Start(dataRoot: copyRoot))
        {
            var status = await incompatible.Get(Status);
            Assert.Equal(JsonValueKind.Null, status.GetProperty("campaign").ValueKind);
            Assert.Equal(JsonValueKind.String, status.GetProperty("recoveryError").ValueKind);
            var entry = Assert.Single((await incompatible.Get("/api/campaigns")).EnumerateArray());
            Assert.Equal(id, entry.GetProperty("id").GetGuid());
            Assert.Equal(JsonValueKind.String, entry.GetProperty("error").ValueKind);
            var failure = await incompatible.Post("/api/campaigns/continue", status, new { campaignId = id },
                HttpStatusCode.UnprocessableEntity);
            Assert.Equal("invalid_campaign_data", failure.GetProperty("code").GetString());
        }

        Assert.Equal(beforeBytes, File.ReadAllBytes(copiedDatabase));
        Assert.Equal("Portable checkpoint", store.LoadLatestValidCheckpoint(id).Checkpoint!.Info.Label);
    }

    [Fact]
    public async Task CompetingSavesAndFailedTransactionsCannotClobberTheCommittedHead()
    {
        await using var host = await Start();
        var created = await host.Create("Concurrent");
        var id = created.GetProperty("campaign").GetProperty("id").GetGuid();
        var a = host.Post("/api/campaigns/save-named", created, new { label = "A" }, HttpStatusCode.Created);
        var b = host.Post("/api/campaigns/save-named", created, new { label = "B" }, HttpStatusCode.Created);
        var saves = await Task.WhenAll(a, b);
        Assert.Equal(new[] { "2", "3" }, saves.Select(save => save.GetProperty("sequence").GetString()).Order());

        var store = new SqliteCampaignStore(root);
        var prior = store.LoadLatestValidCheckpoint(id).Checkpoint!.Info;
        Sql(id, "CREATE TRIGGER fail_save BEFORE UPDATE OF head_sequence ON campaign BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        var failure = await host.Post("/api/campaigns/save", created, new { }, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("storage_unavailable", failure.GetProperty("code").GetString());
        Assert.Equal(prior, store.LoadLatestValidCheckpoint(id).Checkpoint!.Info);
        Assert.Equal(3, store.ListCheckpoints(id).Count);
        Sql(id, "DROP TRIGGER fail_save;");

        var external = store.SaveRecovery(id, prior.Sequence, "external", new SystemLab().ExportState());
        var conflict = await host.Post("/api/campaigns/save", created, new { }, HttpStatusCode.Conflict);
        Assert.Equal("checkpoint_conflict", conflict.GetProperty("code").GetString());
        Assert.Equal(external, store.LoadLatestValidCheckpoint(id).Checkpoint!.Info);
    }

    [Fact]
    public async Task GracefulShutdownCheckpointsDirtyStateEvenWithAutosaveDisabled()
    {
        Guid id;
        await using (var host = await Start(saveOnShutdown: true))
        {
            var created = await host.Create("Shutdown");
            id = created.GetProperty("campaign").GetProperty("id").GetGuid();
            await host.Advance(30);
            Assert.True((await host.Get(Status)).GetProperty("dirty").GetBoolean());
        }

        await using var restarted = await Start(saveOnShutdown: true);
        var status = await restarted.Get(Status);
        Assert.Equal(id, status.GetProperty("campaign").GetProperty("id").GetGuid());
        Assert.False(status.GetProperty("running").GetBoolean());
        Assert.Equal("300000000", status.GetProperty("lastCheckpoint").GetProperty("simulationTicks").GetString());
        Assert.Equal("2", status.GetProperty("campaign").GetProperty("headSequence").GetString());
    }

    [Fact]
    public async Task AbruptProcessExitRestoresLastCommitWithoutSavingDirtyProgress()
    {
        var settings = Path.Combine(root, "persistence.json");
        var process = await Host.StartProcess(root, settings);
        Guid campaignId;
        await using (process)
        {
            var created = await process.Create("Crash recovery");
            campaignId = created.GetProperty("campaign").GetProperty("id").GetGuid();
            await process.Advance(5);
            await process.Post("/api/campaigns/save", await process.Get(Status), new { }, HttpStatusCode.Created);
            await process.Advance(30);
            Assert.True((await process.Get(Status)).GetProperty("dirty").GetBoolean());
        }

        await using var restarted = await Host.StartProcess(root, settings);
        var status = await restarted.Get(Status);
        Assert.Equal(campaignId, status.GetProperty("campaign").GetProperty("id").GetGuid());
        Assert.Equal("2", status.GetProperty("campaign").GetProperty("headSequence").GetString());
        Assert.Equal("50000000", (await restarted.Get(Lab)).GetProperty("world").GetProperty("elapsedTicks").GetString());
        Assert.False(status.GetProperty("running").GetBoolean());
    }

    [Fact]
    public async Task HostedAutosaveCommitsDirtyProgressAndReportsItsConfiguration()
    {
        var settings = Path.Combine(root, "persistence.json");
        File.WriteAllText(settings, """{"autosave":{"intervalSeconds":1},"saveOnShutdown":false}""");
        await using var host = await Host.Start(root, settings);
        var created = await host.Create("Hosted autosave");
        var campaignId = created.GetProperty("campaign").GetProperty("id").GetGuid();
        await host.Advance(5);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        JsonElement status;
        do
        {
            await Task.Delay(25, timeout.Token);
            status = await host.Get(Status);
        }
        while (status.GetProperty("dirty").GetBoolean());

        Assert.Equal(1, status.GetProperty("autosave").GetProperty("intervalSeconds").GetInt32());
        Assert.Equal("recovery", status.GetProperty("lastCheckpoint").GetProperty("kind").GetString());
        var saved = new SqliteCampaignStore(root).LoadLatestValidCheckpoint(campaignId).Checkpoint!.Info;
        Assert.Equal(2, saved.Sequence);
        Assert.Equal(5 * TimeSpan.TicksPerSecond, saved.SimulationTicks);
    }

    [Fact]
    public async Task HttpTicksKeepAllInt64Digits()
    {
        const long ticks = long.MaxValue;
        var state = new SystemLab().ExportState();
        var ship = state.Simulation.Spacecraft[0];
        var checkpoint = state with
        {
            Simulation = state.Simulation with
            {
                ElapsedTicks = ticks,
                Spacecraft = [ship with { FreeCoast = ship.FreeCoast! with { EpochTicks = ticks } }]
            },
            History = [state.History[0] with { AtTicks = ticks }]
        };
        var campaign = new SqliteCampaignStore(root).CreateCampaign("Full precision", "test", checkpoint);

        await using var host = await Start();
        var status = await host.Get(Status);
        Assert.Equal(campaign.Id, status.GetProperty("campaign").GetProperty("id").GetGuid());
        var world = (await host.Get(Lab)).GetProperty("world");
        var expected = ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, world.GetProperty("elapsedTicks").GetString());
        Assert.Equal(expected, world.GetProperty("events")[0].GetProperty("atTicks").GetString());
        Assert.Equal(expected, status.GetProperty("lastCheckpoint").GetProperty("simulationTicks").GetString());
    }

    [Fact]
    public async Task MutationsRequireSameOriginJsonAndExplicitCurrentRevision()
    {
        await using var host = await Start();
        var status = await host.Get(Status);
        using var forbidden = await host.Send("/api/campaigns/new", status, new { displayName = "Wrong origin" }, "http://example.org");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/campaigns/new")
        {
            Content = JsonContent.Create(new { instanceId = status.GetProperty("instanceId").GetString(), displayName = "Missing revision" })
        };
        request.Headers.Add("Origin", host.Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var malformed = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Empty((await host.Get("/api/campaigns")).EnumerateArray());
    }

    private async Task<Host> Start(bool saveOnShutdown = false, string? dataRoot = null)
    {
        dataRoot ??= root;
        var settings = Path.Combine(dataRoot, "persistence.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(new { autosave = new { enabled = false }, saveOnShutdown }));
        return await Host.Start(dataRoot, settings);
    }

    private string Database(Guid id, string? dataRoot = null) => Path.Combine(dataRoot ?? root, id.ToString("N") + ".db");

    private void Sql(Guid campaign, string commandText, string? dataRoot = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Database(campaign, dataRoot),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class Host(HttpClient client, Func<ValueTask> stop) : IAsyncDisposable
    {
        private readonly Guid browser = Guid.NewGuid();
        public HttpClient Client { get; } = client;

        public static async Task<Host> Start(string root, string settings)
        {
            var app = QuasarHost.Build(["--urls", "http://127.0.0.1:0", "--contentRoot", root,
                "--Persistence:DataRoot", root, "--Persistence:SettingsFile", settings, "--Logging:LogLevel:Default", "Warning"]);
            try
            {
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                return new(ClientAt(address), async () =>
                {
                    await app.StopAsync();
                    await app.DisposeAsync();
                });
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public static async Task<Host> StartProcess(string root, string settings)
        {
            var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            var start = new ProcessStartInfo(dotnet)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            string[] args = [typeof(QuasarHost).Assembly.Location, "--urls", "http://127.0.0.1:0",
                "--contentRoot", root, "--Persistence:DataRoot", root, "--Persistence:SettingsFile", settings,
                "--Logging:LogLevel:Default", "Warning", "--Logging:LogLevel:Microsoft.Hosting.Lifetime", "Information"];
            foreach (var arg in args) start.ArgumentList.Add(arg);

            var process = Process.Start(start)!;
            async ValueTask Kill()
            {
                using (process)
                {
                    if (!process.HasExited) process.Kill();
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await process.WaitForExitAsync(deadline.Token);
                }
            }

            try
            {
                const string listening = "Now listening on: ";
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
                {
                    var index = line.IndexOf(listening, StringComparison.Ordinal);
                    if (index >= 0) return new(ClientAt(line[(index + listening.Length)..]), Kill);
                }
                throw new InvalidOperationException("The disposable Quasar process exited before starting Kestrel.");
            }
            catch
            {
                await Kill();
                throw;
            }
        }

        private static HttpClient ClientAt(string address) => new()
        {
            BaseAddress = new(address),
            Timeout = TimeSpan.FromSeconds(15)
        };

        public async Task<JsonElement> Create(string displayName) =>
            await Post("/api/campaigns/new", await Get(Status), new { displayName }, HttpStatusCode.Created);

        public async Task<JsonElement> Get(string route, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await Client.GetAsync(route);
            return await Read(response, status);
        }

        public async Task<JsonElement> Post(string route, JsonElement version, object values, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await Send(route, version, values);
            return await Read(response, status);
        }

        public async Task<HttpResponseMessage> Send(string route, JsonElement version, object values, string? origin = null)
        {
            var body = JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            body.Add("instanceId", version.GetProperty("instanceId"));
            body.Add("revision", version.GetProperty("revision"));
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
            request.Headers.Add("Origin", origin ?? Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
            return await Client.SendAsync(request);
        }

        public async Task<JsonElement> Command(string operation, int? duration = null, string? mode = null, string? previewId = null) =>
            await Post(Lab + "/commands", await Get(Lab), new
            {
                operation,
                clientId = browser,
                durationSeconds = duration,
                mode,
                previewId
            });

        public async Task<JsonElement> Advance(int seconds)
        {
            await Command("configure", duration: seconds, mode: "once");
            await Command("play");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var current = await Get(Lab);
                if (!current.GetProperty("running").GetBoolean()) return current;
                await Task.Delay(25, timeout.Token);
            }
        }

        public static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, received {response.StatusCode}. {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await stop();
        }
    }
}
