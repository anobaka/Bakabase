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
    /// Whether remote callers may start a live ffmpeg transcode. Loopback callers
    /// are never subject to this.
    /// </summary>
    bool GetAllowLiveTranscode();

    Task SetAllowLiveTranscodeAsync(bool allow);

    /// <summary>
    /// The payload discovery and <c>server-info</c> both serve — see
    /// <see cref="RemoteAccessServerDescriptor"/>.
    /// </summary>
    Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync();
}
