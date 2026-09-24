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
    /// of the retired thin client's 34600, so a machine that still runs that one for a
    /// while does not decide either origin by launch order.
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
    /// spinner, and a server that has not answered in three seconds is not going to — the
    /// same bound the thin client's own search used.
    /// </summary>
    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How often a filed management request is checked for an answer.</summary>
    public TimeSpan ClaimPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a request that ended without being approved stays in the listing, so the
    /// page can say what happened to it rather than have it silently vanish.
    /// </summary>
    public TimeSpan FinishedRequestRetention { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Whether starting up brings over the retired thin client's pairings, once.</summary>
    public bool ImportLegacyClientOnStart { get; set; } = true;

    /// <summary>
    /// Where the managed-server store lives. Null for <c>{AppData}/remote-access/managed</c>,
    /// next to the server's own remote-access state.
    /// </summary>
    public string? ManagedDirectory { get; set; }

    /// <summary>
    /// The retired thin client's <c>connection.json</c>. Null to find it the way the thin
    /// client itself did — see <see cref="LegacyClientConnectionSource.ResolveDefaultFile"/>.
    /// </summary>
    public Func<string?>? LegacyClientConnectionFile { get; set; }
}
