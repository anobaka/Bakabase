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
    /// reading back follows (best effort, §7.2.4). Raise <see cref="RaiseInboundGranted"/> first, so data sync hears
    /// of the grant before it hears how the read-back went.
    /// </summary>
    public void ReadBackDataSync(string nodeId) => _ = Task.Run(async () =>
    {
        try { await ReadBackDataSyncAsync(nodeId); }
        catch (Exception e) { logger.LogWarning(e, "Reading back the definitions of {NodeId} failed", nodeId); }
    });

    /// <summary>
    /// How long a read-back may take in all. Each of the (at most eight) addresses a device offers is tried in turn,
    /// every request bounded by the federation client's own deadline, so only a store that stops answering reaches it.
    /// </summary>
    internal static readonly TimeSpan ReadBackBudget = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Takes the offer of a device this node just granted two-way definitions access, and redeems its datasync code
    /// at each address it gave until one works, asking only to follow it (§7.2.4 step 4). Data sync hears how it
    /// went: a granted read-back as this device's own new access (<see cref="IDataSyncGrantEvents.OutboundGranted"/>),
    /// a failed one as <see cref="IDataSyncGrantEvents.ReadBackFailed"/>, so the approver's link can say why it still
    /// waits for access (N14). A device this one already reads needs nothing more, whatever became of the offer, and
    /// is announced as read.
    /// </summary>
    /// <remarks>
    /// It takes no cancellation from its caller. The grant it follows is issued and announced, and the offer is
    /// single-use: once taken nothing can try it again. So whoever asked for the approval going away (a page closed
    /// mid-request) must neither cut the read-back short nor leave data sync without its outcome. It runs on a budget
    /// of its own instead, and a device that does not answer in time counts as unreachable, like one that is not there.
    /// </remarks>
    /// <returns>
    /// Whether this device may now read the device's definitions; if not, why: a <see cref="DataSyncPeerErrorCode"/>
    /// name (<see cref="DataSyncPeerErrorCode.AccessMissing"/> when there was no offer to take), or
    /// <see cref="Bakabase.Modules.DataSync.Services.DataSyncProblemCode.InvitationInvalid"/> when the device refused
    /// its own code.
    /// </returns>
    public async Task<(bool Granted, string? ErrorCode)> ReadBackDataSyncAsync(string nodeId)
    {
        using var budget = new CancellationTokenSource(ReadBackBudget);
        var ct = budget.Token;
        var error = nameof(DataSyncPeerErrorCode.AccessMissing);
        try
        {
            var offer = await peers.TakeDataSyncReciprocalOfferAsync(nodeId, ct);
            foreach (var address in offer?.Addresses ?? [])
            {
                try
                {
                    var outcome = await pairing.ConnectDataSyncAsync(address, offer!.Code, NodeDataSyncIntents.Follow,
                        DataSyncContractOfThisBuild, expectedNodeId: nodeId, ct: ct);
                    if (outcome.Outcome == "granted")
                    {
                        RaiseOutboundGranted(nodeId);
                        return (true, null);
                    }
                    // A reciprocal code grants at once; anything else means the device no longer honours it.
                    error = nameof(DataSyncPeerErrorCode.AccessRevoked);
                    break;
                }
                catch (FederationAccessException e)
                {
                    error = FederationDataSyncGrants.ErrorCodeOf(e);
                    logger.LogInformation("Reading back the definitions of {NodeId} at {Address} failed: {Code}", nodeId,
                        address, FederationDataSyncPeerClient.WireCode(e.ErrorCode) ?? $"http{e.StatusCode}");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The device did not answer within the client's own deadline: the next address may do better.
                    error = nameof(DataSyncPeerErrorCode.Unreachable);
                    logger.LogInformation("Reading back the definitions of {NodeId} at {Address} timed out", nodeId,
                        address);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Best effort: the grant this device issued stands whatever happens here.
                    error = nameof(DataSyncPeerErrorCode.InvalidResponse);
                    logger.LogWarning(e, "Reading back the definitions of {NodeId} at {Address} failed", nodeId, address);
                }
            }
            if (await peers.HasOutboundDataSyncGrantAsync(nodeId, ct))
            {
                // Read already (an earlier approval, or the device's own code): announced like a new grant, so data
                // sync hears the outcome it was promised; a link that already reads the device finds nothing new in it.
                RaiseOutboundGranted(nodeId);
                return (true, null);
            }
        }
        catch (Exception e)
        {
            // The budget ran out, or this device's own state could not be read: data sync still hears the outcome.
            error = nameof(DataSyncPeerErrorCode.Unreachable);
            logger.LogWarning(e, "Reading back the definitions of {NodeId} did not finish", nodeId);
        }
        RaiseReadBackFailed(nodeId, error);
        return (false, error);
    }

    /// <summary>This device's request or code for a device's definitions was granted.</summary>
    /// <param name="readBack">
    /// What the exchange said about the device reading this one back (<see cref="NodeDataSyncReadBack"/>), when it
    /// said anything: a two-way request approved without it leaves this device's link saying it is not read back.
    /// </param>
    public void RaiseOutboundGranted(string peerNodeId, string? readBack = null) =>
        Raise(events => events.OutboundGranted(peerNodeId, readBack));

    /// <summary>This device granted a device <c>datasync.read</c>.</summary>
    /// <param name="readBackStarted">
    /// The grant is two-way and this device reads the device back: raised before that read-back, which then raises
    /// <see cref="RaiseOutboundGranted"/> or the failure.
    /// </param>
    public void RaiseInboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted) =>
        Raise(events => events.InboundGranted(peerNodeId, intent, readBackStarted));

    private void RaiseReadBackFailed(string peerNodeId, string errorCode) =>
        Raise(events => events.ReadBackFailed(peerNodeId, errorCode));

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
            foreach (var outcome in await pairing.ClaimPendingDataSyncOutcomesAsync(ct))
                RaiseOutboundGranted(outcome.PeerNodeId, outcome.ReadBack);
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
