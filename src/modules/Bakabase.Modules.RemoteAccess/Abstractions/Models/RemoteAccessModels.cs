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
    TranscodeDisabled = 4
}

/// <summary>
/// The decision the middleware reached for one request, stashed on the
/// <c>HttpContext</c> so downstream code (and the MVC filters) can read it.
/// </summary>
public record RemoteAccessContext
{
    public required bool IsLoopback { get; init; }
    public required RemoteAccessMode Mode { get; init; }

    /// <summary>
    /// True when this request bypasses every remote check — a loopback caller, or
    /// <see cref="RemoteAccessMode.Unrestricted"/>.
    /// </summary>
    public bool IsUnrestricted => IsLoopback || Mode == RemoteAccessMode.Unrestricted;
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
public record RemoteAccessServerDescriptor(
    string Id,
    string Name,
    int? Port,
    string AppVersion,
    int ProtocolVersion);
