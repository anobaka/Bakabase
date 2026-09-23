using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Remoting.Abstractions.Models;

/// <summary>
/// What a server told this client about itself.
/// </summary>
public sealed record ServerInfo(
    string Id,
    string Name,
    string AppVersion,
    int ProtocolVersion,
    RemoteAccessMode Mode,
    bool PairingSupported,
    DateTime? ServerTime);

/// <summary>
/// Why a handshake did not end in a usable connection. Each one sends the user
/// somewhere different, which is the whole reason they are separate.
/// </summary>
public enum ServerHandshakeOutcome
{
    Ok = 0,

    /// <summary>Nothing answered. Wrong address, server not running, or the network is down.</summary>
    Unreachable = 1,

    /// <summary>Something answered, but not a Bakabase server.</summary>
    NotBakabase = 2,

    /// <summary>The server speaks a newer contract than this client knows. Update the client.</summary>
    ClientTooOld = 3,

    /// <summary>The server is older than anything this client can talk to. Update the server.</summary>
    ServerTooOld = 4,

    /// <summary>Reached, understood, and refusing everyone: remote access is switched off there.</summary>
    RemoteAccessDisabled = 5,

    /// <summary>
    /// This client's own address. Separate from <see cref="NotBakabase"/> because the fix
    /// is the opposite of what that one suggests: the address is not wrong about Bakabase,
    /// it is wrong about which computer.
    /// </summary>
    SelfAddress = 6
}

public sealed record ServerHandshakeResult(ServerHandshakeOutcome Outcome, ServerInfo? Server, string? Detail)
{
    public bool Succeeded => Outcome == ServerHandshakeOutcome.Ok;

    public static ServerHandshakeResult Ok(ServerInfo server) => new(ServerHandshakeOutcome.Ok, server, null);

    public static ServerHandshakeResult Failed(ServerHandshakeOutcome outcome, string? detail = null,
        ServerInfo? server = null) => new(outcome, server, detail);
}
