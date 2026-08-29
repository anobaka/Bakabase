using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Models.Domain.Options;

/// <summary>
/// Controls whether devices other than the host machine may use Bakabase.
/// <para>
/// Nothing here affects loopback requests, so the desktop app is unaffected by
/// every setting on this class.
/// </para>
/// <para>
/// Deliberately just a switch: whatever can reach the port is treated as
/// trusted, so there is no pairing, no per-device credential, and no access list
/// to keep in sync. That makes the LAN itself the security boundary — reasonable
/// on a home network, not something to port-forward.
/// </para>
/// <para>
/// Caveat: "loopback" is decided from the connection's peer address. Putting a
/// reverse proxy in front of Bakabase on the same machine makes every request
/// look local. Bakabase deliberately does not trust forwarded headers, since a
/// client could otherwise claim to be local just by sending one.
/// </para>
/// </summary>
[Options(fileKey: "remote-access")]
public class RemoteAccessOptions
{
    /// <summary>
    /// Explicit mode chosen by the user. Null means "use the runtime default",
    /// which is <see cref="RemoteAccessMode.Unrestricted"/> under Docker (preserving
    /// the behavior containerized installs have always had) and
    /// <see cref="RemoteAccessMode.Disabled"/> everywhere else.
    /// </summary>
    public RemoteAccessMode? Mode { get; set; }

    /// <summary>
    /// Whether remote clients may trigger the live ffmpeg transcode path. Off by
    /// default: remote playback of an incompatible video is meant to be handed to a
    /// native player rather than burning host CPU per viewer.
    /// </summary>
    public bool AllowLiveTranscode { get; set; }

    /// <summary>
    /// Stable identity of this Bakabase install, generated once on first use.
    /// Lets a client recognize "the server I paired my library browsing with"
    /// across IP changes and restarts — the address is transient, this is not.
    /// </summary>
    public string? ServerId { get; set; }
}
