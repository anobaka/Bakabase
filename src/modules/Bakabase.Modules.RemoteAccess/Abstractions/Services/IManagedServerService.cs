using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Services;

/// <summary>Directional paired management connections, shared by desktop and headless hosts.</summary>
/// <remarks>Read-only federation grants never enter this store or authorize these operations.
/// Only desktop hosts additionally provide a loopback UI relay through OpenAsync.</remarks>
public interface IManagedServerService
{
    /// <summary>Send a bounded owner-directed API request through the explicitly paired connection.
    /// API controllers must allowlist operations; this transport never accepts a URL or caller headers.</summary>
    Task<ManagedServerResponse> SendAsync(string serverId, HttpMethod method, string pathAndQuery, string? json,
        CancellationToken ct = default) => throw new NotSupportedException("Managed requests are unavailable.");

    /// <summary>Stream a selected owner file while keeping its authenticated exchange alive.
    /// Only GET/HEAD and a single byte range are allowed; the caller owns the typed file allowlist.</summary>
    Task SendMediaAsync(string serverId, HttpMethod method, string pathAndQuery, string? range,
        Func<HttpResponseMessage, CancellationToken, Task> consume, CancellationToken ct = default) =>
        throw new NotSupportedException("Managed media is unavailable.");

    /// <summary>Desktop-only native opening using this device's resource profiles and path mappings.</summary>
    Task<ManagedServerResponse> PlayManagedResourceAsync(string serverId, int resourceId, DataOrigin origin,
        string key, string libraryEpoch, bool explicitChoice = false, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop player.");

    Task<ManagedServerResponse> OpenManagedResourceDirectoryAsync(string serverId, int resourceId,
        string libraryEpoch, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop file manager.");

    Task<ManagedServerResponse> BatchPlayManagedResourcesAsync(string serverId, int[] resourceIds,
        string libraryEpoch, string? playerKey, int fileSelectionMode, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop player.");

    /// <summary>Read-only, bounded resource-profile search operations on a managed source.
    /// The operation is a fixed name, never a caller-supplied URL. No local player configuration is sent.</summary>
    Task<string> ReadResourceProfileSourceAsync(string serverId, string operation, string? json, string? libraryEpoch = null,
        CancellationToken ct = default) => throw new NotSupportedException("Source resource profiles are unavailable.");

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

    /// <summary>
    /// Stops managing every server, here only: drops every management request this device filed,
    /// deletes every key and stops every relay. No server is told anything — nothing is sent to
    /// any of them.
    /// </summary>
    /// <remarks>
    /// For "Make this a new device" on an install whose data directory was copied from another:
    /// each key came with the copy and is the original install's too, and the original still
    /// manages those servers with it. Asking a server to revoke it, as <see cref="ForgetAsync"/>
    /// does, would cut the original off as well. The servers go on listing that one device;
    /// the copy pairs again with the ones it should manage.
    /// <para>
    /// Throws when the store cannot be written. No server is forgotten then, though the filed
    /// requests are already dropped.
    /// </para>
    /// </remarks>
    /// <returns>How many servers were forgotten.</returns>
    Task<int> ForgetAllLocallyAsync(CancellationToken ct = default);

    Task<bool> SetPathMappingsAsync(string serverId, IReadOnlyList<ManagedServerPathMapping> mappings,
        CancellationToken ct = default);

    /// <summary>
    /// Starts the server's relay if needed and returns where the window should navigate.
    /// </summary>
    /// <param name="path">A path on the server's UI to land on, e.g. <c>/resource</c>. Defaults to its root.</param>
    /// <returns>Null for a server this device does not manage.</returns>
    Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default);
}
