using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quasar.Server;
using Quasar.Server.Notifications;

namespace Quasar.Simulation.Tests;

public sealed class ArrivalNotificationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("quasar-arrival-").FullName;
    private const string Api = "/api/notifications/";

    [Fact]
    public async Task DeviceOptInSurvivesRestartAndRequiresAValidAppleSubscription()
    {
        var device = AppleDevice();
        string publicKey;
        await using (var app = QuasarHost.Build(Arguments()))
        {
            await app.StartAsync();
            using var http = Connect(app.Services);
            publicKey = await ReadPublicKey(http);
            Assert.False(await Enabled(http, device));
            Assert.Equal(HttpStatusCode.Forbidden, await Post(http, "subscribe", device, "https://other.example"));
            Assert.Equal(HttpStatusCode.BadRequest, await Post(http, "subscribe", device with { Endpoint = "https://127.0.0.1/push" }));
            Assert.Equal(HttpStatusCode.BadRequest, await Post(http, "subscribe", device with { Endpoint = "https://push.apple.com.evil.example/push" }));
            Assert.Equal(HttpStatusCode.BadRequest, await Post(http, "subscribe", device with { Auth = "invalid" }));
            Assert.False(await Enabled(http, device));
            Assert.Equal(HttpStatusCode.NoContent, await Post(http, "subscribe", device));
            Assert.Equal(HttpStatusCode.NoContent, await Post(http, "subscribe", device));
            Assert.True(await Enabled(http, device));
            await app.StopAsync();
        }
        await using var restarted = QuasarHost.Build(Arguments());
        await restarted.StartAsync();
        using var reconnected = Connect(restarted.Services);
        Assert.Equal(publicKey, await ReadPublicKey(reconnected));
        Assert.True(await Enabled(reconnected, device));
        Assert.Equal(HttpStatusCode.NoContent, await Post(reconnected, "unsubscribe", new { device.Endpoint }));
        Assert.False(await Enabled(reconnected, device));
        await restarted.StopAsync();
    }

    [Fact]
    public async Task RunnerPausesBeforeSendingWebPushAndRemovesExpiredDevice()
    {
        var store = new ArrivalSubscriptionStore(Path.Combine(root, "arrival-notifications.json"));
        var device = AppleDevice();
        Assert.True(store.Register(device));
        var world = new SystemLab();
        world.OrderTravel(SolarSystem.Earth);
        var session = new LabSession(world);
        var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new PushHandler(async (request, cancellationToken) =>
        {
            try
            {
                var paused = session.Snapshot();
                Assert.False(paused.Running);
                Assert.Equal("arrival", paused.Interrupt?.Kind);
                Assert.Equal(device.Endpoint, request.RequestUri!.AbsoluteUri);
                Assert.Equal("vapid", request.Headers.Authorization!.Scheme);
                Assert.Equal("3600", Assert.Single(request.Headers.GetValues("TTL")));
                var content = Assert.IsAssignableFrom<HttpContent>(request.Content);
                Assert.Equal("aes128gcm", Assert.Single(content.Headers.ContentEncoding));
                Assert.NotEmpty(await content.ReadAsByteArrayAsync(cancellationToken));
                submitted.SetResult();
            }
            catch (Exception error) { submitted.SetException(error); }
            return new HttpResponseMessage(HttpStatusCode.Gone);
        }));
        using var delivery = new ArrivalNotifications(store, new PushServiceClient(http), NullLogger<ArrivalNotifications>.Instance);
        using var runner = new LabRunner(session, delivery);
        var initial = session.Snapshot();
        var client = Guid.NewGuid().ToString();
        var ready = session.Execute(new(initial.InstanceId, initial.Revision, client, "configure", 86400, "continuous"));
        session.Execute(new(ready.InstanceId, ready.Revision, client, "play"));
        await delivery.StartAsync(default);
        await runner.StartAsync(default);
        try
        {
            await submitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (store.Read()!.Devices.Length != 0) await Task.Delay(10, deadline.Token);
            Assert.Null(session.TickWithArrival());
        }
        finally
        {
            await runner.StopAsync(default);
            await delivery.StopAsync(default);
        }
    }

    private string[] Arguments() => ["--urls", "http://127.0.0.1:0", "--contentRoot", root,
        "--Persistence:DataRoot", root, "--Persistence:SettingsFile", Path.Combine(root, "settings.json"),
        "--Logging:LogLevel:Default", "Warning"];

    private static HttpClient Connect(IServiceProvider services)
    {
        var address = services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(5) };
    }

    private static async Task<string> ReadPublicKey(HttpClient http)
    {
        using var response = await http.GetAsync(Api + "key");
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("publicKey", Assert.Single(json.RootElement.EnumerateObject()).Name);
        return json.RootElement.GetProperty("publicKey").GetString()!;
    }

    private static async Task<bool> Enabled(HttpClient http, ArrivalSubscription device)
    {
        using var response = await Send(http, "status", new { device.Endpoint });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean();
    }

    private static async Task<HttpStatusCode> Post(HttpClient http, string route, object body, string? origin = null)
    {
        using var response = await Send(http, route, body, origin);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient http, string route, object body, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api + route) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", origin ?? http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        return await http.SendAsync(request);
    }

    private static ArrivalSubscription AppleDevice()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        byte[] point = [4, .. parameters.Q.X!, .. parameters.Q.Y!];
        return new("https://web.push.apple.com/test-endpoint", WebEncoders.Base64UrlEncode(point),
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)));
    }

    private sealed class PushHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
