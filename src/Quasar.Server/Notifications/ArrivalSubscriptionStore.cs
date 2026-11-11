using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Quasar.Server.Notifications;

internal sealed record ArrivalSubscription(string Endpoint, string P256dh, string Auth)
{
    public static bool IsAppleEndpoint(string? endpoint) =>
        endpoint is { Length: <= 2048 } && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.IdnHost.EndsWith(".push.apple.com", StringComparison.Ordinal)
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath.Length > 1;

    public bool IsValid()
    {
        if (!IsAppleEndpoint(Endpoint) || P256dh is not { Length: 87 } || Auth is not { Length: 22 }) return false;
        try
        {
            var point = WebEncoders.Base64UrlDecode(P256dh);
            if (point is not [4, ..] || point.Length != 65 || WebEncoders.Base64UrlDecode(Auth).Length != 16) return false;
            using var key = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..65] }
            });
            return true;
        }
        catch (Exception error) when (error is FormatException or CryptographicException) { return false; }
    }
}

internal sealed record ArrivalSubscriptionState(string PublicKey, string PrivateKey, ArrivalSubscription[] Devices);

internal sealed class ArrivalSubscriptionStore(string path)
{
    public const int DeviceLimit = 16;
    private readonly object gate = new();
    private ArrivalSubscriptionState? state;

    public ArrivalSubscriptionState? Read()
    {
        lock (gate) return ReadCore();
    }

    public string PublicKey()
    {
        lock (gate) return (ReadCore() ?? Create()).PublicKey;
    }

    public bool Register(ArrivalSubscription device)
    {
        lock (gate)
        {
            var current = ReadCore() ?? Create();
            if (current.Devices.Contains(device)) return true;
            var otherDevices = current.Devices.Where(other => other.Endpoint != device.Endpoint).ToArray();
            if (otherDevices.Length >= DeviceLimit) return false;
            Save(current with { Devices = [.. otherDevices, device] });
            return true;
        }
    }

    public void Remove(string endpoint, ArrivalSubscription? expiredDevice = null)
    {
        lock (gate)
        {
            if (ReadCore() is not { } current) return;
            var devices = current.Devices.Where(device =>
                device.Endpoint != endpoint || (expiredDevice is not null && expiredDevice != device)).ToArray();
            if (devices.Length != current.Devices.Length) Save(current with { Devices = devices });
        }
    }

    private ArrivalSubscriptionState? ReadCore()
    {
        if (state is not null) return state;
        try
        {
            using var file = File.OpenRead(path);
            var saved = JsonSerializer.Deserialize<ArrivalSubscriptionState>(file);
            if (saved is not { PublicKey.Length: 87, PrivateKey.Length: 43, Devices.Length: <= DeviceLimit }
                || saved.Devices.Any(device => device is null || !device.IsValid()))
                throw new InvalidDataException("Invalid arrival notification state.");
            return state = saved;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private ArrivalSubscriptionState Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        byte[] publicPoint = [4, .. parameters.Q.X!, .. parameters.Q.Y!];
        return Save(new(WebEncoders.Base64UrlEncode(publicPoint), WebEncoders.Base64UrlEncode(parameters.D!), []));
    }

    private ArrivalSubscriptionState Save(ArrivalSubscriptionState next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.CreateNew };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = File.Open(temporaryPath, options))
            {
                JsonSerializer.Serialize(file, next);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            return state = next;
        }
        finally { File.Delete(temporaryPath); }
    }
}
