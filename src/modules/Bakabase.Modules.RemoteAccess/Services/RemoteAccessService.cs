using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Services;

public class RemoteAccessService(
    IBOptionsManager<RemoteAccessOptions> optionsManager,
    RemoteAccessDefaults defaults,
    ILogger<RemoteAccessService> logger) : IRemoteAccessService
{
    private const string PathTokenVersion = "1";
    private static readonly TimeSpan DefaultPairingCodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Last time each device's <c>LastSeenAt</c> was written to disk, so a browsing
    /// session does not rewrite the options file on every request.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastSeenPersistedAt = new();

    private readonly SemaphoreSlim _secretLock = new(1, 1);

    private RemoteAccessOptions Options => optionsManager.Value;

    public RemoteAccessMode GetEffectiveMode() => Options.Mode ?? defaults.Mode;

    public async Task SetModeAsync(RemoteAccessMode? mode)
    {
        if (mode == RemoteAccessMode.Authenticated)
        {
            // Authenticated mode signs media URLs, so make sure a key exists before
            // any request depends on one.
            await EnsureSigningSecretAsync();
        }

        await optionsManager.SaveAsync(o => o.Mode = mode);
        logger.LogInformation("Remote access mode set to {Mode} (effective: {Effective})", mode, GetEffectiveMode());
    }

    #region Pairing

    public async Task<PairingCodeInfo> IssuePairingCodeAsync(TimeSpan? lifetime = null)
    {
        await EnsureSigningSecretAsync();

        var code = GeneratePairingCode();
        var expiresAt = DateTime.UtcNow.Add(lifetime ?? DefaultPairingCodeLifetime);

        await optionsManager.SaveAsync(o =>
        {
            o.PairingCode = code;
            o.PairingCodeExpiresAt = expiresAt;
        });

        return new PairingCodeInfo(code, expiresAt);
    }

    public PairingCodeInfo? GetPairingCode()
    {
        var options = Options;
        if (string.IsNullOrEmpty(options.PairingCode) || options.PairingCodeExpiresAt == null ||
            options.PairingCodeExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return new PairingCodeInfo(options.PairingCode, options.PairingCodeExpiresAt.Value);
    }

    public async Task<RemoteDevicePairingResult?> PairAsync(string? pairingCode, string? deviceName,
        RemoteDevicePlatform platform)
    {
        var active = GetPairingCode();
        if (active == null || string.IsNullOrEmpty(pairingCode) ||
            !FixedTimeEquals(active.Code, pairingCode))
        {
            return null;
        }

        var token = GenerateToken();
        var device = new RemoteDevice
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(deviceName) ? "Unnamed device" : deviceName.Trim(),
            Platform = platform,
            TokenHash = HashToken(token),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        await optionsManager.SaveAsync(o =>
        {
            o.Devices.Add(device);
            // One code pairs one device.
            o.PairingCode = null;
            o.PairingCodeExpiresAt = null;
        });

        logger.LogInformation("Paired remote device {Name} ({Platform}, {Id})", device.Name, device.Platform,
            device.Id);

        return new RemoteDevicePairingResult(device, token);
    }

    #endregion

    #region Devices

    public RemoteDevice? Authenticate(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = HashToken(token);
        // Every candidate is compared, and each comparison is constant time, so
        // neither the match position nor the device count leaks through timing.
        RemoteDevice? match = null;
        foreach (var device in Options.Devices)
        {
            if (FixedTimeEquals(device.TokenHash, hash))
            {
                match = device;
            }
        }

        return match;
    }

    public IReadOnlyList<RemoteDevice> GetDevices() => Options.Devices.ToArray();

    public async Task RevokeDeviceAsync(string deviceId)
    {
        await optionsManager.SaveAsync(o => o.Devices.RemoveAll(d => d.Id == deviceId));
        _lastSeenPersistedAt.TryRemove(deviceId, out _);
        logger.LogInformation("Revoked remote device {Id}", deviceId);
    }

    public async Task RenameDeviceAsync(string deviceId, string name)
    {
        await optionsManager.SaveAsync(o =>
        {
            var device = o.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device != null)
            {
                device.Name = name;
            }
        });
    }

    public async Task TouchDeviceAsync(string deviceId)
    {
        var now = DateTime.UtcNow;
        var lastPersisted = _lastSeenPersistedAt.GetOrAdd(deviceId, DateTime.MinValue);
        if (now - lastPersisted < RemoteDevice.LastSeenPersistenceInterval)
        {
            return;
        }

        // Claim the write before doing it, so concurrent requests for the same device
        // do not all decide to persist.
        if (!_lastSeenPersistedAt.TryUpdate(deviceId, now, lastPersisted))
        {
            return;
        }

        await optionsManager.SaveAsync(o =>
        {
            var device = o.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device != null)
            {
                device.LastSeenAt = now;
            }
        });
    }

    #endregion

    #region Signed path tokens

    public async Task<string> SignPathTokenAsync(string deviceId, string path, TimeSpan lifetime)
    {
        var secret = await EnsureSigningSecretAsync();
        var normalized = RemotePathNormalizer.Normalize(path)
                         ?? throw new ArgumentException($"Cannot sign an unusable path: {path}", nameof(path));
        var expiry = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var signature = Sign(secret, deviceId, normalized, expiry);

        return $"{PathTokenVersion}.{deviceId}.{expiry}.{signature}";
    }

    public bool TryValidatePathToken(string? token, string? path, out RemoteDevice? device)
    {
        device = null;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 4 || parts[0] != PathTokenVersion)
        {
            return false;
        }

        if (!long.TryParse(parts[2], out var expiry) ||
            DateTimeOffset.FromUnixTimeSeconds(expiry) <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        var secret = Options.SigningSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var normalized = RemotePathNormalizer.Normalize(path);
        if (normalized == null)
        {
            return false;
        }

        var deviceId = parts[1];
        var expected = Sign(secret, deviceId, normalized, expiry);
        if (!FixedTimeEquals(expected, parts[3]))
        {
            return false;
        }

        // A token outlives the device it was issued to only until the next request:
        // revoking a device invalidates its outstanding links too.
        device = Options.Devices.FirstOrDefault(d => d.Id == deviceId);
        return device != null;
    }

    private static string Sign(string secret, string deviceId, string normalizedPath, long expiry)
    {
        var payload = $"{PathTokenVersion}\n{deviceId}\n{normalizedPath}\n{expiry}";
        var hmac = HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hmac);
    }

    private async Task<string> EnsureSigningSecretAsync()
    {
        var existing = Options.SigningSecret;
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        await _secretLock.WaitAsync();
        try
        {
            existing = Options.SigningSecret;
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await optionsManager.SaveAsync(o => o.SigningSecret = secret);
            return secret;
        }
        finally
        {
            _secretLock.Release();
        }
    }

    #endregion

    #region Primitives

    private static string GenerateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string GeneratePairingCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion
}
