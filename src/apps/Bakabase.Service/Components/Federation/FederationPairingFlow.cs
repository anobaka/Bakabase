using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// The parts of pairing that should not depend on a page staying open: claiming approved
/// requests, reading back a device that offered it, and turning browsing on once this device
/// can read another one.
/// </summary>
/// <remarks>
/// Definitions access (<c>datasync.read</c>) goes through the same loop: its requests are claimed next to the
/// library's, and a device that offered to be read back is read back with a datasync code. Whenever this device
/// obtains or grants definitions access, the data sync runtime hears of it (<see cref="IDataSyncGrantEvents"/>), so
/// its links react within seconds; the federation module itself never calls data sync. The events are resolved when
/// raised, not when this flow is built: the runtime that handles them reaches definitions access through this flow.
/// </remarks>
public sealed class FederationPairingFlow(NodePairingClient pairing, FederationPeerService peers,
    FederationBrowsingControl browsing, IRemoteAccessService remoteAccess, ILogger<FederationPairingFlow> logger,
    IServiceProvider? services = null)
    : BackgroundService
{
    private static readonly TimeSpan ClaimInterval = TimeSpan.FromSeconds(5);

    /// <summary>This build's data sync contract, which a peer's must meet before this device asks it for anything.</summary>
    public static readonly NodeDataSyncContract DataSyncContractOfThisBuild =
        new(DataSyncContract.Version, DataSyncContract.MinimumPeerVersion);

    /// <summary>After this device obtained read access, browsing is what the user is here for.</summary>
    public async Task OnOutboundGrantedAsync(CancellationToken ct)
    {
        if (!await browsing.IsEnabledAsync(ct)) await browsing.SetEnabledAsync(true, ct);
    }

    /// <summary>
    /// The local addresses a device on <paramref name="target"/>'s network can use to reach this one,
    /// same-subnet first, one port per interface.
    /// </summary>
    public IReadOnlyList<string> GetShareBackAddresses(string target)
    {
        var host = Uri.TryCreate(FederationAddress(target), UriKind.Absolute, out var uri) ? uri.Host : "";
        IPAddress.TryParse(host, out var targetIp);
        return remoteAccess.GetReachableAddresses()
            .Select(a => a.Url)
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .GroupBy(url => new Uri(url).Host)
            .Select(g => g.First())
            .OrderByDescending(url => targetIp == null ? 0 : SharedPrefixBits(targetIp, new Uri(url).Host))
            .Take(8)
            .ToArray();
    }

    /// <summary>Connects back to a device this node just granted, if it offered read access in return.</summary>
    public void ReadBack(string nodeId) => _ = Task.Run(async () =>
    {
        try
        {
            var offer = await peers.TakeReciprocalOfferAsync(nodeId);
            if (offer == null) return;
            foreach (var address in offer.Addresses)
            {
                try
                {
                    var outcome = await pairing.ConnectAsync(address, offer.Code, expectedNodeId: nodeId);
                    if (outcome.Outcome == "granted") await OnOutboundGrantedAsync(CancellationToken.None);
                    return;
                }
                catch (FederationAccessException e)
                {
                    logger.LogInformation("Reading back {NodeId} at {Address} failed: {Code}", nodeId, address, e.ErrorCode);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Reading back {NodeId} failed", nodeId);
        }
    });

    /// <summary>
    /// <see cref="ReadBackDataSyncAsync"/> in the background: a code redeemed two-way is answered at once, and the
    /// reading back follows (best effort, §7.2.4).
    /// </summary>
    public void ReadBackDataSync(string nodeId) => _ = Task.Run(async () =>
    {
        try { await ReadBackDataSyncAsync(nodeId, CancellationToken.None); }
        catch (Exception e) { logger.LogWarning(e, "Reading back the definitions of {NodeId} failed", nodeId); }
    });

    /// <summary>
    /// Takes the offer of a device this node just granted two-way definitions access, and redeems its datasync code
    /// at each address it gave until one works, asking only to follow it (§7.2.4 step 4). A granted read-back is
    /// told to data sync as this device's own new access.
    /// </summary>
    /// <returns>Whether this device may now read the device's definitions; if not, the last error's code.</returns>
    public async Task<(bool Granted, string? ErrorCode)> ReadBackDataSyncAsync(string nodeId, CancellationToken ct)
    {
        var offer = await peers.TakeDataSyncReciprocalOfferAsync(nodeId, ct);
        if (offer == null) return (false, "NoReciprocalOffer");
        string? error = null;
        foreach (var address in offer.Addresses)
        {
            try
            {
                var outcome = await pairing.ConnectDataSyncAsync(address, offer.Code, NodeDataSyncIntents.Follow,
                    DataSyncContractOfThisBuild, expectedNodeId: nodeId, ct: ct);
                if (outcome.Outcome != "granted") return (false, outcome.Outcome);
                RaiseOutboundGranted(nodeId);
                return (true, null);
            }
            catch (FederationAccessException e)
            {
                error = e.ErrorCode;
                logger.LogInformation("Reading back the definitions of {NodeId} at {Address} failed: {Code}", nodeId,
                    address, e.ErrorCode);
            }
        }
        return (false, error);
    }

    /// <summary>This device's request or code for a device's definitions was granted.</summary>
    public void RaiseOutboundGranted(string peerNodeId) => Raise(events => events.OutboundGranted(peerNodeId));

    /// <summary>This device granted a device <c>datasync.read</c>.</summary>
    public void RaiseInboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted) =>
        Raise(events => events.InboundGranted(peerNodeId, intent, readBackStarted));

    private void Raise(Action<IDataSyncGrantEvents> raise)
    {
        try
        {
            if (services?.GetService<IDataSyncGrantEvents>() is { } events) raise(events);
        }
        catch (Exception e)
        {
            // The runtime reacts on its next scheduled attempt instead; pairing itself succeeded.
            logger.LogWarning(e, "Data sync did not take a grant event");
        }
    }

    /// <summary>
    /// One round of the claim loop: this device's waiting library requests, then its waiting definitions requests,
    /// each on its own route. A definitions grant claimed here is told to data sync at once.
    /// </summary>
    public async Task ClaimPendingAsync(CancellationToken ct)
    {
        try
        {
            if (await pairing.ClaimPendingAsync(ct)) await OnOutboundGrantedAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            // Unreadable sharing state is reported by the Devices page; keep retrying quietly.
            logger.LogDebug(e, "Claiming pending pairing requests failed");
        }
        try
        {
            foreach (var nodeId in await pairing.ClaimPendingDataSyncAsync(ct)) RaiseOutboundGranted(nodeId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            logger.LogDebug(e, "Claiming pending definitions requests failed");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ClaimInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await ClaimPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private static string FederationAddress(string address)
    {
        try { return Bakabase.Modules.Federation.Transport.FederationHttpClient.NormalizeAddress(address); }
        catch (FederationAccessException) { return ""; }
    }

    private static int SharedPrefixBits(IPAddress target, string host)
    {
        if (!IPAddress.TryParse(host, out var candidate) || candidate.AddressFamily != target.AddressFamily) return 0;
        var a = target.GetAddressBytes();
        var b = candidate.GetAddressBytes();
        var bits = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] ^ b[i];
            if (diff == 0) { bits += 8; continue; }
            bits += BitOperations.LeadingZeroCount((uint)diff) - 24;
            break;
        }
        return bits;
    }
}
