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

    /// <summary>
    /// The loopback port this server's relay listens on in the desktop app, once it has
    /// had one. Kept with the server because the browser keys localStorage, IndexedDB and
    /// its cache to the origin, port included: a server whose relay moved would look to
    /// the user like it had forgotten their settings. Never set by the thin client, which
    /// has exactly one listener of its own.
    /// </summary>
    public int? RelayPort { get; set; }

    /// <summary>
    /// Brought over from the retired thin client on this machine rather than paired here.
    /// Informational: the UI says where a server came from, nothing behaves differently.
    /// </summary>
    public bool ImportedFromLegacyClient { get; set; }
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

    /// <summary>
    /// When the desktop app last brought over the retired thin client's pairings. Set once
    /// something was found, so the automatic import at startup runs only once: a server the
    /// user has since stopped managing must not come back on the next launch.
    /// </summary>
    public DateTime? LegacyClientImportedAt { get; set; }

    /// <summary>
    /// The relay ports of servers the desktop app stopped managing, by server id. The browser
    /// still holds each one's storage under that origin, so the port is kept from the next
    /// server paired here — whose own code would otherwise read what the last one's UI left —
    /// and given back if the same server is paired again. Null when there are none, and
    /// never set by the thin client.
    /// </summary>
    public Dictionary<string, int>? RetiredRelayPorts { get; set; }
}
