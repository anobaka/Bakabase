using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Services;

public interface IRemoteAccessService
{
    /// <summary>
    /// The mode actually in force: the user's explicit choice, or the runtime
    /// default when they have not made one.
    /// </summary>
    RemoteAccessMode GetEffectiveMode();

    Task SetModeAsync(RemoteAccessMode? mode);

    /// <summary>
    /// Issues (or re-issues) the short code a new device presents to pair. Any
    /// previously outstanding code stops working.
    /// </summary>
    Task<PairingCodeInfo> IssuePairingCodeAsync(TimeSpan? lifetime = null);

    /// <summary>
    /// The outstanding pairing code, or null when none is active or it has expired.
    /// </summary>
    PairingCodeInfo? GetPairingCode();

    /// <summary>
    /// Exchanges a valid pairing code for a device token. Returns null when the code
    /// is wrong or expired. The code is consumed on success.
    /// </summary>
    Task<RemoteDevicePairingResult?> PairAsync(string? pairingCode, string? deviceName,
        RemoteDevicePlatform platform);

    /// <summary>
    /// Matches a raw device token against the paired devices. Returns null when it
    /// matches nothing. Comparison is constant time.
    /// </summary>
    RemoteDevice? Authenticate(string? token);

    IReadOnlyList<RemoteDevice> GetDevices();

    Task RevokeDeviceAsync(string deviceId);

    Task RenameDeviceAsync(string deviceId, string name);

    /// <summary>
    /// Records that a device is active. Persists at most once per
    /// <see cref="RemoteDevice.LastSeenPersistenceInterval"/> per device, so the hot
    /// path does not rewrite the options file.
    /// </summary>
    Task TouchDeviceAsync(string deviceId);

    /// <summary>
    /// Mints a token that authorizes exactly one path for one device until it
    /// expires. Used for URLs opened by something that cannot carry a cookie or
    /// header — a native player handed a link, a &lt;video&gt; on another origin.
    /// </summary>
    Task<string> SignPathTokenAsync(string deviceId, string path, TimeSpan lifetime);

    /// <summary>
    /// Validates a token minted by <see cref="SignPathTokenAsync"/> against the path
    /// being requested, and yields the device it was issued to.
    /// </summary>
    bool TryValidatePathToken(string? token, string? path, out RemoteDevice? device);
}
