using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// Why a remote request was turned away. Surfaced to the client so the UI can say
/// something useful instead of a bare 403.
/// </summary>
public enum RemoteAccessDenialReason
{
    None = 0,

    /// <summary>Remote access is switched off entirely.</summary>
    Disabled = 1,

    /// <summary>
    /// The endpoint runs on the host machine (launching a player, opening a folder,
    /// deleting files) and would land on a screen the caller cannot see.
    /// </summary>
    HostOnly = 2,

    /// <summary>The requested path is outside every media library and cache root.</summary>
    PathNotServable = 3,

    /// <summary>
    /// The video needs a live ffmpeg transcode and the host has not allowed remote
    /// callers to start one. The client should hand the stream to a native player
    /// instead.
    /// </summary>
    TranscodeDisabled = 4,

    /// <summary>
    /// The action's effect lands on whatever machine runs it — launching a player,
    /// opening a folder, showing a window — so running it here would put something on
    /// a screen the caller cannot see. Unlike <see cref="HostOnly"/> this is refused
    /// even in <see cref="RemoteAccessMode.Unrestricted"/> and even for a paired
    /// device: it is not a permission, it is a statement about where the action means
    /// anything.
    /// </summary>
    RunsOnUserMachine = 5,

    /// <summary>
    /// Pairing is required here and this caller has none. Distinct from
    /// <see cref="Disabled"/>: the door exists, the caller just has no key.
    /// </summary>
    Unauthenticated = 6,

    /// <summary>
    /// The signature's timestamp is outside the accepted window. Nearly always a wrong
    /// clock rather than an attack, and the UI should say so — telling someone their
    /// pairing is broken when their clock is off sends them to the wrong fix.
    /// </summary>
    SignatureExpired = 7,

    /// <summary>
    /// Signed as a device the server no longer knows. The device was revoked, or the
    /// server's data was reset.
    /// </summary>
    DeviceRevoked = 8
}

/// <summary>
/// The decision the middleware reached for one request, stashed on the
/// <c>HttpContext</c> so downstream code (and the MVC filters) can read it.
/// </summary>
public record RemoteAccessContext
{
    public required bool IsLoopback { get; init; }
    public required RemoteAccessMode Mode { get; init; }

    /// <summary>The device that signed this request, if any.</summary>
    public RemoteDevice? Device { get; init; }

    /// <summary>
    /// True when this request bypasses every remote check — a loopback caller, or
    /// <see cref="RemoteAccessMode.Unrestricted"/>.
    /// </summary>
    public bool IsUnrestricted => IsLoopback || Mode == RemoteAccessMode.Unrestricted;

    /// <summary>
    /// Whether a known device signed this request.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="IsUnrestricted"/> rather than folded into
    /// it. That property has other consumers — live transcoding is gated on it, among
    /// them — and collapsing the two would quietly hand paired devices permissions
    /// nobody decided to give them.
    /// </remarks>
    public bool IsPaired => Device != null;
}

/// <summary>
/// One address a phone or another PC can type to reach this Bakabase.
/// </summary>
/// <param name="Url">e.g. <c>http://192.168.1.5:34567</c>.</param>
/// <param name="InterfaceName">The network interface it belongs to, to help pick.</param>
public record RemoteAccessAddress(string Url, string InterfaceName);

/// <summary>
/// What this install tells other devices about itself — the payload behind the
/// discovery beacon and the <c>server-info</c> endpoint, kept identical so a
/// client learns the same facts whichever way it found the server.
/// </summary>
/// <param name="Id">Stable install identity; survives IP changes and restarts.</param>
/// <param name="Name">Human-readable name to show in a server picker.</param>
/// <param name="Port">
/// First listening port, or null before Kestrel has reported one. The port is
/// chosen at runtime on desktop installs, which is why discovery has to carry it.
/// </param>
/// <param name="AppVersion">Application version, informational.</param>
/// <param name="ProtocolVersion">
/// Remote API contract version (<see cref="RemoteAccessProtocol.CurrentVersion"/>);
/// clients compare it against the range they support before talking further.
/// </param>
/// <param name="Kind">
/// What kind of install it is, for showing. Optional: older servers do not say, and nothing
/// is decided on it.
/// </param>
/// <param name="Platform">The operating system it runs on, for showing. Optional, like the kind.</param>
public record RemoteAccessServerDescriptor(
    string Id,
    string Name,
    int? Port,
    string AppVersion,
    int ProtocolVersion,
    ServerKind? Kind = null,
    RemoteDevicePlatform? Platform = null);
