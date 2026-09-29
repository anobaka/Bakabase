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
    /// Addresses another device on the same network can open, one per non-loopback
    /// interface and listening port. Used to tell the user what to type into a phone.
    /// </summary>
    IReadOnlyList<RemoteAccessAddress> GetReachableAddresses();

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
