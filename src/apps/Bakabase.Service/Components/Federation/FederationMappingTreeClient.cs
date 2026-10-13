using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;

namespace Bakabase.Service.Components.Federation;

/// <summary>Browses one explicitly configured peer under its read-only grant; no user-supplied URL.</summary>
public sealed class FederationMappingTreeClient(IPeerSessionFactory sessions, INodeTransport transport, GrantLeaseRegistry leases)
{
    public async Task<MappingTreePage> ReadAsync(string nodeId, string? parent, string? cursor, int limit, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(nodeId)) throw new FederationQueryException("InvalidMappingTreeRequest", 422);
        FederationMappingTreeService.ValidateRequest(parent, cursor, limit);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var peer = await sessions.GetAsync(nodeId, deadline.Token);
        if (peer.NodeId != nodeId || peer.Info.NodeId != nodeId || peer.Credentials.AudienceNodeId != nodeId ||
            peer.LibraryEpoch != peer.Info.LibraryEpoch || peer.Scope != FederationScopes.LibraryRead)
            throw new FederationQueryException("InvalidPeerResponse", 502);
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(peer.GrantId)));
        var parameters = new List<string> { "limit=" + limit };
        if (parent != null) parameters.Add("parent=" + Uri.EscapeDataString(parent));
        if (cursor != null) parameters.Add("cursor=" + Uri.EscapeDataString(cursor));
        using var response = await transport.SendAsync(peer, HttpMethod.Get,
            "/federation/v1/export/mapping-tree?" + string.Join('&', parameters), cancellationToken: authorized.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new FederationQueryException("MappingTreeUnavailable", 501,
                "This peer does not support directory mapping trees. Use its existing root mappings or update it.");
        var result = await FederationHttpClient.ReadEnvelopeAsync<MappingTreePage>(response, authorized.Token);
        if (result.Nodes is not { Length: <= FederationMappingTreeService.MaximumPageSize } || result.Nodes.Length > limit ||
            result.Nodes.Any(x => x == null || !NodeRequestSignature.IsIdentifier(x.SourceRootId) ||
                x.SourceRootId.Length != 65 || !x.SourceRootId.StartsWith('g') || !x.SourceRootId[1..].All(char.IsAsciiHexDigit) ||
                x.Name is not { Length: > 0 and <= 1024 } || x.ResourceCount <= 0 || string.IsNullOrEmpty(x.BrowseToken)) ||
            result.Nodes.Select(x => x.SourceRootId).Distinct(StringComparer.Ordinal).Count() != result.Nodes.Length ||
            JsonSerializer.SerializeToUtf8Bytes(result).Length > FederationMappingTreeService.MaximumResponseBytes)
            throw new FederationQueryException("InvalidPeerResponse", 502);
        try
        {
            foreach (var item in result.Nodes) FederationMappingTreeTokens.ValidateInput(item.BrowseToken);
            FederationMappingTreeTokens.ValidateInput(result.NextCursor);
        }
        catch (FederationQueryException e) { throw new FederationQueryException("InvalidPeerResponse", 502, innerException: e); }
        authorized.Token.ThrowIfCancellationRequested();
        return result;
    }
}
