using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Services;

/// <summary>
/// Other servers this device manages in full, through a signed loopback relay per server
/// that shows each server's own UI in this device's window.
/// </summary>
/// <remarks>
/// <para>
/// Implemented only by the desktop app, which composes the relays; a headless server
/// registers nothing and its endpoints answer that management is unavailable. The
/// Service therefore resolves this optionally and never references the implementation.
/// </para>
/// <para>
/// Management is the legacy paired-device access: full control of the other server,
/// except actions that run on the user's own machine — those run here, against this
/// device's own player configuration and path mappings.
/// </para>
/// </remarks>
public interface IManagedServerService
{
    /// <param name="probe">Also ask every server how it is, bounded to a couple of seconds.</param>
    Task<ManagedServersView> GetAsync(bool probe, CancellationToken ct = default);

    /// <summary>Asks an address what it is. Never throws for an unreachable address — that is an answer.</summary>
    Task<ManagedServerProbeView> ProbeAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Looks for servers on this network that could be managed from here, for a few seconds.
    /// </summary>
    /// <remarks>
    /// Uses the remote-access beacons, the ones the removed thin client found servers by, so a
    /// server shows up whether or not it shares its library. Never lists this device itself.
    /// Runs only when asked: nothing probes the network on its own.
    /// </remarks>
    Task<ManagedServerDiscoveryView> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Pairs with a code shown on the other server, or files a request for it to approve.</summary>
    /// <remarks>
    /// A filed request is collected in the background until it is approved, rejected or
    /// lapses; the result carries its id so the UI can show it waiting.
    /// </remarks>
    Task<ManagedServerPairingView> PairAsync(string address, string? code, CancellationToken ct = default);

    /// <summary>Stops waiting on a filed request. The other side simply sees it lapse.</summary>
    Task<bool> CancelRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Stops managing a server: deletes the key, stops its relay, then asks it to revoke this
    /// device — best effort, and only if its address still answers as that server.
    /// </summary>
    Task<bool> ForgetAsync(string serverId, CancellationToken ct = default);

    Task<bool> SetPathMappingsAsync(string serverId, IReadOnlyList<ManagedServerPathMapping> mappings,
        CancellationToken ct = default);

    /// <summary>
    /// Starts the server's relay if needed and returns where the window should navigate.
    /// </summary>
    /// <param name="path">A path on the server's UI to land on, e.g. <c>/resource</c>. Defaults to its root.</param>
    /// <returns>Null for a server this device does not manage.</returns>
    Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default);

    /// <summary>
    /// Brings over the servers the removed thin client on this machine was paired with,
    /// keys included, so nothing has to be paired again. Runs once on its own at startup;
    /// this re-runs it on request. Never overwrites a server already managed here.
    /// </summary>
    Task<ManagedServerImportView> ImportFromLegacyClientAsync(CancellationToken ct = default);
}
