using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Definitions access (<c>datasync.read</c>) for the data sync runtime, over the federation peer service and pairing
/// client (§7.1, §7.2). It only ever touches definitions access: library grants stay on the loopback
/// <c>/federation/local</c> API and the CLI, so nothing that reaches data sync can create or change them
/// (<c>DataSyncGrantBoundaryTests</c>).
/// </summary>
/// <remarks>
/// Expected failures on this device are thrown as <see cref="DataSyncProblemException"/>, what a peer answered as
/// <see cref="DataSyncPeerException"/>; the facade turns both into answers.
/// </remarks>
public sealed class FederationDataSyncGrants(FederationPeerService peers, NodePairingClient pairing,
    FederationPairingFlow flow, FederationStateStore store, INodeIdentityProvider identity,
    IRemoteAccessService remoteAccess, IBOptionsManager<RemoteAccessOptions> remoteOptions, PeerSessionFactory sessions,
    INodePeerDiscovery discovery) : IDataSyncGrantService
{
    public Task<bool> IsSharingEnabledAsync(CancellationToken ct) => store.IsDataSyncSharingEnabledAsync(ct);

    /// <summary>
    /// The switch, and with <paramref name="enablePairedRemoteAccess"/> remote access with pairing required — but only
    /// from Disabled (§7.1.3, N3). A mode the user opened further (Enabled, or a Docker install's Unrestricted) is
    /// never touched, unlike the library wizard's request, which writes Enabled whatever the mode was (F68).
    /// </summary>
    public async Task SetSharingEnabledAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct)
    {
        if (enabled && enablePairedRemoteAccess && remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
            await EnablePairedRemoteAccessAsync(ct);
        await peers.SetDataSyncSharingAsync(enabled, ct);
    }

    public Task<RemoteAccessMode> GetRemoteAccessModeAsync(CancellationToken ct) =>
        Task.FromResult(remoteAccess.GetEffectiveMode());

    /// <summary>Every device known here, and with <paramref name="discover"/> the ones answering on the network.</summary>
    public async Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct)
    {
        var status = await peers.GetDataSyncStatusAsync(ct);
        var candidates = status.Peers.ToDictionary(p => p.NodeId, p => new DataSyncPeerCandidate(p.NodeId, p.Name,
            p.Address, Known: true, Discovered: false, ContractVersion: null, SharesDefinitions: null,
            WeMayRead: p.WeMayRead, TheyMayRead: p.TheyMayRead, LinkId: null,
            ConnectionState: sessions.GetConnectionState(p.NodeId, FederationScopes.DataSyncRead)), StringComparer.Ordinal);
        if (discover)
            foreach (var found in await discovery.DiscoverAsync(ct))
                candidates[found.NodeId] = candidates.TryGetValue(found.NodeId, out var known)
                    ? known with
                    {
                        Discovered = true, Address = known.Address ?? found.Address,
                        ContractVersion = found.DataSyncContractVersion, SharesDefinitions = found.SharesDefinitions
                    }
                    : new DataSyncPeerCandidate(found.NodeId, found.Name, found.Address, Known: false, Discovered: true,
                        found.DataSyncContractVersion, found.SharesDefinitions, WeMayRead: false, TheyMayRead: false,
                        LinkId: null, ConnectionState: null);
        return candidates.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Asks a device for its definitions, by request or with its code. Two-way also offers this device back with a
    /// reciprocal datasync code, which needs definitions sharing and remote access on here (§7.2.4 step 1).
    /// </summary>
    public async Task<DataSyncAccessRequestOutcome> RequestAccessAsync(DataSyncAccessRequestInput input,
        CancellationToken ct)
    {
        var twoWay = input.Intent == DataSyncRequestIntent.TwoWay;
        var address = input.Address;
        if (string.IsNullOrWhiteSpace(address))
            address = (await peers.GetDataSyncStatusAsync(ct)).Peers
                .FirstOrDefault(p => p.NodeId == input.PeerNodeId)?.Address ?? throw Problem(
                    DataSyncProblemCode.PeerUnreachable, "No address is known for this device. Enter its address.");
        IReadOnlyList<string>? reciprocal = null;
        if (twoWay)
        {
            if (!await store.IsDataSyncSharingEnabledAsync(ct))
                throw Problem(DataSyncProblemCode.SharingOff,
                    "Keeping in step both ways lets the other device read this one: turn definitions sharing on first.");
            RequireRemoteAccess();
            reciprocal = flow.GetShareBackAddresses(address);
            if (reciprocal.Count == 0)
                throw Problem(DataSyncProblemCode.RemoteAccessOff,
                    "This device has no network address the other device could use to read it back.");
        }
        NodeDataSyncPairingOutcome outcome;
        try
        {
            outcome = await pairing.ConnectDataSyncAsync(address, input.Code,
                twoWay ? NodeDataSyncIntents.TwoWay : NodeDataSyncIntents.Follow,
                FederationPairingFlow.DataSyncContractOfThisBuild, reciprocal,
                string.IsNullOrWhiteSpace(input.PeerNodeId) ? null : input.PeerNodeId, ct);
        }
        catch (FederationAccessException e)
        {
            throw MapPeer(e);
        }
        if (outcome.Outcome == "granted") flow.RaiseOutboundGranted(outcome.PeerNodeId);
        return new DataSyncAccessRequestOutcome(outcome.Outcome, outcome.RequestId, outcome.PeerNodeId,
            outcome.PeerName, outcome.ReadBack);
    }

    public async Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) =>
        (await peers.GetDataSyncStatusAsync(ct)).Requests.Select(r =>
        {
            var incoming = r.Direction == "incoming";
            var claimsKnown = incoming && IsElsewhere(r.KnownAddress, r.RemoteAddress);
            return new DataSyncAccessRequestView(r.RequestId,
                incoming ? DataSyncRequestDirection.Incoming : DataSyncRequestDirection.Outgoing, r.NodeId, r.NodeName,
                IntentOf(r.Intent), r.Status, r.ExpiresAt.UtcDateTime, r.RemoteAddress, claimsKnown,
                claimsKnown ? r.KnownAddress : null, r.ReplacesExistingAccess);
        }).ToArray();

    /// <summary>
    /// Issues the requester <c>datasync.read</c>. With <paramref name="readBack"/>, a two-way request's offer is taken
    /// and this device reads the requester back (best effort: a failure is reported, the grant stands). Without it,
    /// the offer is dropped unused.
    /// </summary>
    /// <remarks>
    /// Data sync hears of the grant before the read-back starts, announcing it, and then of how the read-back went
    /// (<see cref="FederationPairingFlow.ReadBackDataSyncAsync"/>): a two-way request approved to receive back makes
    /// the approver's link even when the read-back then fails (N14). Approving again reads back again only while the
    /// offer is still there; a device this one already reads counts as read back.
    /// </remarks>
    public async Task<DataSyncApprovalOutcome> ApproveAsync(string requestId, bool readBack, CancellationToken ct)
    {
        RequireRemoteAccess();
        NodeDataSyncApproval approval;
        try
        {
            approval = await peers.ApproveDataSyncAsync(requestId, ct);
        }
        catch (FederationAccessException e) when (IsLocalProblem(e))
        {
            throw LocalProblem(e);
        }
        var intent = IntentOf(approval.Intent);
        var receiveBack = intent == DataSyncRequestIntent.TwoWay && readBack;
        flow.RaiseInboundGranted(approval.NodeId, intent, receiveBack);
        var readBackGranted = false;
        string? readBackError = null;
        if (receiveBack) (readBackGranted, readBackError) = await flow.ReadBackDataSyncAsync(approval.NodeId, ct);
        else if (approval.HasReciprocal) await peers.TakeDataSyncReciprocalOfferAsync(approval.NodeId, ct);
        return new DataSyncApprovalOutcome(approval.NodeId,
            await peers.GetPeerNameAsync(approval.NodeId, ct) ?? approval.NodeName, intent, readBackGranted,
            readBackError);
    }

    public async Task RejectAsync(string requestId, CancellationToken ct)
    {
        if (!await peers.RejectDataSyncAsync(requestId, ct))
            throw Problem(DataSyncProblemCode.RequestNotFound, "This request no longer exists.");
    }

    public async Task CancelOutgoingAsync(string requestId, CancellationToken ct)
    {
        if (!await peers.CancelOutgoingDataSyncAsync(requestId, ct))
            throw Problem(DataSyncProblemCode.RequestNotFound, "This request is no longer waiting.");
    }

    public async Task<IReadOnlyList<DataSyncGrantView>> GetGrantsAsync(CancellationToken ct) =>
        (await peers.GetDataSyncStatusAsync(ct)).Grants
        .Select(g => new DataSyncGrantView(g.NodeId, g.Name, g.GrantedAt.UtcDateTime)).ToArray();

    public Task RevokeAsync(string peerNodeId, CancellationToken ct) => peers.RevokeDataSyncAsync(peerNodeId, ct);

    /// <summary>A code needs both switches: one that cannot work is never shown (§7.2.3).</summary>
    public async Task<DataSyncInvitationView> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct)
    {
        if (!await store.IsDataSyncSharingEnabledAsync(ct))
            throw Problem(DataSyncProblemCode.SharingOff, "Turn definitions sharing on before creating a code.");
        RequireRemoteAccess();
        NodeInvitation invitation;
        try
        {
            invitation = await peers.IssueDataSyncInvitationAsync(input.AllowTwoWay, ct);
        }
        catch (FederationAccessException e) when (IsLocalProblem(e))
        {
            throw LocalProblem(e);
        }
        return new DataSyncInvitationView(invitation.Code, invitation.ExpiresAt.UtcDateTime, ReachableAddresses(),
            input.AllowTwoWay);
    }

    public Task<bool> HasOutboundGrantAsync(string peerNodeId, CancellationToken ct) =>
        peers.HasOutboundDataSyncGrantAsync(peerNodeId, ct);

    public Task ForgetOutboundAsync(string peerNodeId, CancellationToken ct) =>
        peers.ForgetOutboundDataSyncAsync(peerNodeId, ct);

    /// <summary>As the library's sharing wizard writes it, keeping the install's legacy identity and settings.</summary>
    private async Task EnablePairedRemoteAccessAsync(CancellationToken ct)
    {
        // Capture the existing settings before initializing either identity: the legacy file watcher can briefly
        // publish defaults during its own write.
        var configured = remoteOptions.Value;
        var allowLiveTranscode = configured.AllowLiveTranscode;
        var legacyServerId = configured.ServerId;
        if (string.IsNullOrWhiteSpace(legacyServerId))
            legacyServerId = await remoteAccess.GetOrCreateServerIdAsync();
        await identity.GetAsync(ct);
        await remoteOptions.SaveAsync(new RemoteAccessOptions
        {
            // A cloned federation node intentionally has a different NodeId; never replace the legacy server identity.
            ServerId = legacyServerId,
            AllowLiveTranscode = allowLiveTranscode,
            RequirePairing = true,
            Mode = RemoteAccessMode.Enabled
        });
    }

    /// <summary>A device that refuses every node request can neither be read nor read back (§7.2.4).</summary>
    private void RequireRemoteAccess()
    {
        if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
            throw Problem(DataSyncProblemCode.RemoteAccessOff,
                "Remote access is off on this device, so no other device can reach it. Turn it on with pairing required.");
    }

    /// <summary>One address per host, as the library shows next to its code.</summary>
    private IReadOnlyList<string> ReachableAddresses() => remoteAccess.GetReachableAddresses().Select(a => a.Url)
        .GroupBy(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url)
        .Select(g => g.First()).ToArray();

    /// <summary>Whether an incoming request came from somewhere other than where this device knows the node it names.</summary>
    private static bool IsElsewhere(string? knownAddress, string? remoteAddress)
    {
        if (knownAddress == null || remoteAddress == null ||
            !Uri.TryCreate(knownAddress, UriKind.Absolute, out var known)) return false;
        var host = known.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var knownIp) && IPAddress.TryParse(remoteAddress, out var remoteIp))
            return !Normalize(knownIp).Equals(Normalize(remoteIp));
        return !string.Equals(host, remoteAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static DataSyncRequestIntent IntentOf(string intent) =>
        intent == NodeDataSyncIntents.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow;

    /// <summary>
    /// What a peer's refusal of a pairing means to data sync (the mapping of §7.6), and this device's own refusals
    /// on the way (a wrong code, an address that is not one) as the problem they are.
    /// </summary>
    internal static Exception MapPeer(FederationAccessException e) => e.ErrorCode switch
    {
        "InvalidPairingCode" => Problem(DataSyncProblemCode.InvitationInvalid, e.Message),
        "InvalidAddress" => Problem(DataSyncProblemCode.PeerUnreachable, e.Message),
        "PeerTooOld" or "NodeRouteForbidden" or "ProtocolUnsupported" =>
            new DataSyncPeerException(DataSyncPeerErrorCode.PeerTooOld, e.Message),
        "ThisTooOld" => new DataSyncPeerException(DataSyncPeerErrorCode.ThisTooOld, e.Message),
        "PeerSharingOff" or "SharingDisabled" or "DataSyncSharingDisabled" =>
            new DataSyncPeerException(DataSyncPeerErrorCode.PeerSharingOff, e.Message),
        "RemoteAccessDisabled" => new DataSyncPeerException(DataSyncPeerErrorCode.PeerRemoteAccessOff, e.Message),
        "NodeUnreachable" => new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, e.Message),
        "IdentityConflict" or "SelfAddress" => new DataSyncPeerException(DataSyncPeerErrorCode.IdentityConflict, e.Message),
        "LibraryEpochChanged" => new DataSyncPeerException(DataSyncPeerErrorCode.PeerReset, e.Message),
        "GrantRevoked" or "InvalidNodeSignature" or "PairingRejected" =>
            new DataSyncPeerException(DataSyncPeerErrorCode.AccessRevoked, e.Message),
        "PairingBusy" or "PairingRateLimited" => new DataSyncPeerException(DataSyncPeerErrorCode.Busy, e.Message),
        "NodeResponseTooLarge" => new DataSyncPeerException(DataSyncPeerErrorCode.TooLarge, e.Message),
        _ when e.StatusCode == 503 => new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, e.Message),
        _ => new DataSyncPeerException(DataSyncPeerErrorCode.InvalidResponse, e.Message)
    };

    /// <summary>
    /// <see cref="MapPeer"/> as the one word data sync keeps for it: a <see cref="DataSyncPeerErrorCode"/> name, or a
    /// <see cref="DataSyncProblemCode"/> name for this device's own refusals on the way.
    /// </summary>
    internal static string ErrorCodeOf(FederationAccessException e) => MapPeer(e) switch
    {
        DataSyncPeerException peer => peer.Code.ToString(),
        DataSyncProblemException problem => problem.Problem.Code.ToString(),
        _ => nameof(DataSyncPeerErrorCode.InvalidResponse)
    };

    /// <summary>This device's own peer service refusing a change of definitions access.</summary>
    private static bool IsLocalProblem(FederationAccessException e) =>
        e.ErrorCode is "RequestNotFound" or "DataSyncSharingDisabled";

    private static DataSyncProblemException LocalProblem(FederationAccessException e) => e.ErrorCode == "RequestNotFound"
        ? Problem(DataSyncProblemCode.RequestNotFound, e.Message)
        : Problem(DataSyncProblemCode.SharingOff, "Definitions sharing is off on this device.");

    private static DataSyncProblemException Problem(DataSyncProblemCode code, string? detail) =>
        new(new DataSyncProblem(code, detail));
}
