using Bakabase.Remoting.Components.Forwarding;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// How the desktop app runs the servers it manages. Composition-time settings, not user
/// settings: every default is what the shipped app uses, and tests are what change them.
/// </summary>
/// <remarks>
/// Deliberately a plain object handed to <see cref="RemoteConsoleServiceCollectionExtensions.AddRemoteConsole"/>
/// rather than an <c>[Options]</c> type. Options are broadcast to every UI hub client and
/// written to the settings files; nothing here belongs in either, and the store it points
/// at holds device keys.
/// </remarks>
public sealed class RemoteConsoleOptions
{
    /// <summary>
    /// Where relay ports start.
    /// </summary>
    /// <remarks>
    /// Clear of the app's own listening window (34567 and up, three ports by default) and
    /// of the removed thin client's 34600, which an old install left on the machine may
    /// still hold, so neither decides an origin by launch order.
    /// </remarks>
    public const int DefaultFirstRelayPort = 34650;

    public int FirstRelayPort { get; set; } = DefaultFirstRelayPort;

    /// <summary>How far past <see cref="FirstRelayPort"/> to look for a free port.</summary>
    public int RelayPortRange { get; set; } = 256;

    /// <summary>
    /// How long asking a server how it is may take, end to end. A listing with probing
    /// runs while somebody watches it, and a server that has not answered in two seconds
    /// is, for that purpose, not there.
    /// </summary>
    public TimeSpan ProbeBudget { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long looking for servers on the network waits for answers. Somebody is watching a
    /// spinner, and a server that has not answered in three seconds is not going to.
    /// </summary>
    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How often a filed management request is checked for an answer.</summary>
    public TimeSpan ClaimPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a request that ended without being approved stays in the listing, so the
    /// page can say what happened to it rather than have it silently vanish.
    /// </summary>
    public TimeSpan FinishedRequestRetention { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a relay trusts that its server's address answers as that server. A page in
    /// use is asked again in the background past half of it; nothing is forwarded past all of
    /// it until the address has answered again.
    /// </summary>
    public TimeSpan IdentityCheckInterval { get; set; } = UpstreamIdentityPolicy.Default.Lifetime;

    /// <summary>
    /// How long a relay stands by an address answering as someone else, or not at all,
    /// before asking again — so a server that comes back is noticed within this, and a page
    /// reloaded meanwhile does not ask on every request.
    /// </summary>
    public TimeSpan IdentityRetryInterval { get; set; } = UpstreamIdentityPolicy.Default.RetryInterval;

    /// <summary>How long a relay waits for its server's address to say who it is.</summary>
    public TimeSpan IdentityCheckTimeout { get; set; } = UpstreamIdentityPolicy.Default.Timeout;

    /// <summary>
    /// How recent that answer has to be for a relay to open a new connection to its server —
    /// the moment the process at the other end can have changed.
    /// </summary>
    public TimeSpan IdentityConnectionWindow { get; set; } = UpstreamIdentityPolicy.Default.ConnectionWindow;

    /// <summary>
    /// How long to wait before trying again when noting in the managed-server store that a
    /// server answered (its name, when it was last seen) could not be written — a full disk,
    /// a file a scanner holds. Nothing waits on that write; it is tried again until it lands.
    /// </summary>
    public TimeSpan StoreRetryInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether starting up brings over the removed thin client's pairings, once.</summary>
    public bool ImportLegacyClientOnStart { get; set; } = true;

    /// <summary>
    /// Where the managed-server store lives. Null for <c>{AppData}/remote-access/managed</c>,
    /// next to the server's own remote-access state.
    /// </summary>
    public string? ManagedDirectory { get; set; }

    /// <summary>
    /// The removed thin client's <c>connection.json</c>. Null to find it the way the thin
    /// client itself did — see <see cref="LegacyClientConnectionSource.ResolveDefaultFile"/>.
    /// </summary>
    public Func<string?>? LegacyClientConnectionFile { get; set; }
}
