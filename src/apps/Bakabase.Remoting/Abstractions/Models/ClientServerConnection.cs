using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Remoting.Abstractions.Models;

/// <summary>
/// One server this client has paired with, and the credentials to reach it.
/// </summary>
/// <remarks>
/// Deliberately not an <c>[Options]</c> type: it carries the device key. On the server
/// side that rule exists because options are broadcast to every UI hub client; here it
/// is simpler than that — a key belongs in one file that only this process reads, not in
/// the settings blob the UI renders.
/// </remarks>
public class ClientServerConnection
{
    /// <summary>
    /// The server's stable install identity. Checked on every reconnect: an address can
    /// be reused by a different install (a new container on the same port, a reset), and
    /// signing with credentials that no longer belong there would just fail confusingly.
    /// </summary>
    public string ServerId { get; set; } = null!;

    /// <summary>What the server calls itself, for a picker. Refreshed on connect.</summary>
    public string? ServerName { get; set; }

    /// <summary>e.g. <c>http://192.168.1.5:34567</c>. No trailing slash.</summary>
    public string BaseAddress { get; set; } = null!;

    public string DeviceId { get; set; } = null!;

    /// <summary>base64url of the HMAC key this device signs with.</summary>
    public string DeviceKey { get; set; } = null!;

    public DateTime PairedAt { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    /// <summary>
    /// Where this server's libraries are on this machine. Per server, because the same
    /// client may reach two installs whose paths mean entirely different things.
    /// </summary>
    public List<ClientPathMapping> PathMappings { get; set; } = [];
}

/// <summary>
/// Everything <c>connection.json</c> holds.
/// </summary>
public class ClientConnectionData
{
    /// <summary>
    /// Servers this client knows, newest first. A list rather than a single entry
    /// because a laptop that follows its owner between a home server and a work one
    /// should not have to re-pair on every move.
    /// </summary>
    public List<ClientServerConnection> Servers { get; set; } = [];

    /// <summary>Which server is in use, by <see cref="ClientServerConnection.ServerId"/>.</summary>
    public string? ActiveServerId { get; set; }

    /// <summary>
    /// How this device introduces itself when pairing. Stored so a rename on the server
    /// is not undone the next time the client pairs somewhere else.
    /// </summary>
    public string? DeviceName { get; set; }

    public RemoteDevicePlatform Platform { get; set; }
}
