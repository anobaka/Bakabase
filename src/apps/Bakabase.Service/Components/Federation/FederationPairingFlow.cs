using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// The parts of pairing that should not depend on a page staying open: claiming approved
/// requests, reading back a device that offered it, and turning browsing on once this device
/// can read another one.
/// </summary>
public sealed class FederationPairingFlow(NodePairingClient pairing, FederationPeerService peers,
    FederationBrowsingControl browsing, IRemoteAccessService remoteAccess, ILogger<FederationPairingFlow> logger)
    : BackgroundService
{
    private static readonly TimeSpan ClaimInterval = TimeSpan.FromSeconds(5);

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ClaimInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (await pairing.ClaimPendingAsync(stoppingToken)) await OnOutboundGrantedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                // Unreadable sharing state is reported by the Devices page; keep retrying quietly.
                logger.LogDebug(e, "Claiming pending pairing requests failed");
            }
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
