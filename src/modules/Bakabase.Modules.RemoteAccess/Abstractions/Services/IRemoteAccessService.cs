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

    /// <summary>
    /// Sets the mode, or null to fall back to the runtime default.
    /// </summary>
    Task SetModeAsync(RemoteAccessMode? mode);

    /// <summary>
    /// Candidate origins another device can open, ordered by explicit choice, browser,
    /// deployment and native interface. Container interfaces are never advertised.
    /// </summary>
    IReadOnlyList<RemoteAccessAddress> GetReachableAddresses();

    string? GetAdvertisedAddress();
    /// <summary>Persists an explicit origin; null restores automatic selection. Invalid input is refused.</summary>
    Task SetAdvertisedAddressAsync(string? address);
    /// <summary>Remembers an operator's actual browser origin briefly, without writing settings.</summary>
    void ObserveAddress(string address);


    /// <summary>
    /// This install's stable identity, generated and persisted on first use.
    /// </summary>
    Task<string> GetOrCreateServerIdAsync();

    /// <summary>
    /// Replaces this install's identity with a new one, for a copy of another install's data
    /// directory that must stop answering as that install. Returns the new identity.
    /// </summary>
    Task<string> RegenerateServerIdAsync();

    /// <summary>
    /// Whether remote callers may start a live ffmpeg transcode. Loopback callers
    /// are never subject to this.
    /// </summary>
    bool GetAllowLiveTranscode();

    Task SetAllowLiveTranscodeAsync(bool allow);

    /// <summary>
    /// Whether an unpaired caller is refused outright. Never applies to loopback.
    /// </summary>
    bool GetRequirePairing();

    Task SetRequirePairingAsync(bool require);

    /// <summary>
    /// The payload discovery and <c>server-info</c> both serve — see
    /// <see cref="RemoteAccessServerDescriptor"/>.
    /// </summary>
    Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync();
}
