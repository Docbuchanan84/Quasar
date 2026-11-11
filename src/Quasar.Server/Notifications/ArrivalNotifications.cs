using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Quasar.Server.Notifications;

internal sealed class ArrivalNotifications(ArrivalSubscriptionStore store, PushServiceClient client,
    ILogger<ArrivalNotifications> logger) : BackgroundService
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(16);

    public void Enqueue(string arrival)
    {
        if (!queue.Writer.TryWrite(arrival)) logger.LogWarning("Arrival notification queue is full.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var tokens = new VapidTokenCache();
        client.AutoRetryAfter = false;
        await foreach (var arrival in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (store.Read() is not { Devices.Length: > 0 } current) continue;
                using var authentication = new VapidAuthentication(current.PublicKey, current.PrivateKey)
                {
                    Subject = "https://github.com/Docbuchanan84/Quasar",
                    TokenCache = tokens
                };
                var message = new PushMessage(JsonSerializer.Serialize(new
                {
                    title = "Quasar · Ship arrived",
                    body = arrival + " Game paused."
                }))
                {
                    TimeToLive = 3600,
                    Urgency = PushMessageUrgency.High
                };
                foreach (var device in current.Devices)
                    await Send(device, message, authentication, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Could not prepare an arrival notification ({ErrorType}).", error.GetType().Name);
            }
        }
    }

    private async Task Send(ArrivalSubscription device, PushMessage message, VapidAuthentication authentication,
        CancellationToken cancellationToken)
    {
        // A device can opt out while an earlier device's delivery is in progress.
        if (store.Read()?.Devices.Contains(device) != true) return;
        try
        {
            await client.RequestPushMessageDeliveryAsync(new PushSubscription
            {
                Endpoint = device.Endpoint,
                Keys = new Dictionary<string, string> { ["p256dh"] = device.P256dh, ["auth"] = device.Auth }
            }, message, authentication, cancellationToken);
        }
        catch (PushServiceClientException error) when (error.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            store.Remove(device.Endpoint, device);
        }
        catch (PushServiceClientException error)
        {
            logger.LogWarning("Apple rejected an arrival notification (HTTP {StatusCode}).", (int)error.StatusCode);
        }
        catch (Exception error) when (error is HttpRequestException or CryptographicException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Could not send an arrival notification ({ErrorType}).", error.GetType().Name);
        }
    }

    private sealed class VapidTokenCache : IVapidTokenCache, IDisposable
    {
        private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 32 });

        public string Get(string audience) => cache.Get<string>(audience)!;
        public void Put(string audience, DateTimeOffset expiration, string token) =>
            cache.Set(audience, token, new MemoryCacheEntryOptions { AbsoluteExpiration = expiration.AddMinutes(-1), Size = 1 });
        public void Dispose() => cache.Dispose();
    }
}
