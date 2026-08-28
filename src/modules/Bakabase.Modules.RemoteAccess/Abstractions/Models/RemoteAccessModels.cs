using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// A freshly issued pairing code and when it stops working.
/// </summary>
public record PairingCodeInfo(string Code, DateTime ExpiresAt);

/// <summary>
/// Result of a successful pairing. <see cref="Token"/> is the only time the raw
/// token exists — only its hash is persisted.
/// </summary>
public record RemoteDevicePairingResult(RemoteDevice Device, string Token);

/// <summary>
/// Why a remote request was turned away. Surfaced to the client so the UI can say
/// something useful instead of a bare 403.
/// </summary>
public enum RemoteAccessDenialReason
{
    None = 0,

    /// <summary>Remote access is switched off entirely.</summary>
    Disabled = 1,

    /// <summary>No device token, or one that no longer matches a paired device.</summary>
    Unauthenticated = 2,

    /// <summary>
    /// The endpoint runs on the host machine (launching a player, opening a folder,
    /// deleting files) and has no meaning for a remote client.
    /// </summary>
    HostOnly = 3,

    /// <summary>The requested path is outside every media library and cache root.</summary>
    PathNotServable = 4
}

/// <summary>
/// The decision the middleware reached for one request, stashed on the
/// <c>HttpContext</c> so downstream code (and the MVC filter) can read it.
/// </summary>
public record RemoteAccessContext
{
    public required bool IsLoopback { get; init; }
    public required RemoteAccessMode Mode { get; init; }
    public RemoteDevice? Device { get; init; }

    /// <summary>
    /// True when this request bypasses every remote check — a loopback caller, or
    /// <see cref="RemoteAccessMode.Open"/>.
    /// </summary>
    public bool IsUnrestricted => IsLoopback || Mode == RemoteAccessMode.Open;
}
