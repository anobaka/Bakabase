using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Remoting.Components.Forwarding;

namespace Bakabase.Modules.RemoteAccess.Components.Management;

/// <summary>Composition-time settings for directional managed connections. Never broadcast as user options.</summary>
public class ManagedServerOptions
{
    public Func<IEnumerable<int>> LocalPorts { get; set; } = () => [];

    /// <summary>Budget for a managed API response, or headers only when streaming media.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
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

    /// <summary>
    /// Whether an address reaches this machine. What tells an address that answers with this
    /// install's own id apart: this device, or another machine with the same identity — a copy
    /// of this data directory. Tests put a server on "another machine" through it.
    /// </summary>
    public Func<Uri, CancellationToken, Task<bool>> ReachesThisMachine { get; set; } = ThisMachine.ReachedByAsync;

    /// <summary>
    /// How every connection to a managed server is opened — the console's questions, pairing and
    /// probes, and every relay's. Tests put a network of their own under the real handlers
    /// through it: a name that resolves where they say.
    /// </summary>
    public DualStackConnector Connector { get; set; } = DualStackConnector.Default;

    /// <summary>
    /// Where the managed-server store lives. Null for <c>{AppData}/remote-access/managed</c>,
    /// next to the server's own remote-access state.
    /// </summary>
    public string? ManagedDirectory { get; set; }
}
