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
    PathNotServable = 3
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
