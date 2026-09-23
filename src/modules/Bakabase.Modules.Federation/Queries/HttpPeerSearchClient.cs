using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;

namespace Bakabase.Modules.Federation.Queries;

/// <summary>One verified peer/session per query; never follows the application's active connection.</summary>
public sealed class HttpPeerSearchClient(INodeTransport transport, PeerSessionSnapshot session) : IPeerSearchClient
{
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private const string Route = "/federation/v1/export/queries";

    public Task<NodeQueryBlock> CreateAsync(NodeExportQuery query, CancellationToken cancellationToken) =>
        ReadBlock(HttpMethod.Post, Route, query with
        {
            BlockSize = Math.Min(query.BlockSize, session.Info.MaxBatchSize)
        }, cancellationToken);

    public Task<NodeQueryBlock> ReadAsync(string snapshotId, string cursor, CancellationToken cancellationToken) =>
        ReadBlock(HttpMethod.Get, $"{Route}/{Uri.EscapeDataString(snapshotId)}/pages?cursor={Uri.EscapeDataString(cursor)}",
            null, cancellationToken);

    public async Task ValidateAsync(string snapshotId, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Post, $"{Route}/{Uri.EscapeDataString(snapshotId)}/validate", null,
            cancellationToken);
        await CheckResponse(response, cancellationToken);
    }

    public async Task ReleaseAsync(string snapshotId, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Delete, $"{Route}/{Uri.EscapeDataString(snapshotId)}", null,
            cancellationToken);
        await CheckResponse(response, cancellationToken);
    }

    private async Task<NodeQueryBlock> ReadBlock(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await Send(method, path, body, ct);
        await CheckResponse(response, ct);
        var bytes = await ReadBounded(response.Content, ct);
        try
        {
            return JsonSerializer.Deserialize<NodeQueryBlock>(bytes.Span, FederationJson.Options) ??
                   throw new JsonException("Missing query block.");
        }
        catch (JsonException exception)
        {
            throw new FederationQueryException("InvalidPeerResponse", 502,
                "The node returned an invalid query response.", nodeId: session.NodeId, innerException: exception);
        }
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try { return await transport.SendAsync(session, method, path, body, ct); }
        catch (FederationAccessException exception)
        {
            throw new FederationQueryException(exception.ErrorCode, exception.StatusCode, exception.Message,
                nodeId: session.NodeId, innerException: exception);
        }
    }

    private async Task CheckResponse(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var code = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "GrantRevoked",
            System.Net.HttpStatusCode.Forbidden => "CapabilityDenied",
            System.Net.HttpStatusCode.Gone => "QuerySessionExpired",
            _ => "PeerUnavailable"
        };
        var retryable = (int)response.StatusCode is 429 or 502 or 503 or 504;
        string? message = null;
        var bytes = await ReadBounded(response.Content, ct);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The error body is not an object.");
            if (json.RootElement.TryGetProperty("code", out var wireCode) && wireCode.ValueKind == JsonValueKind.String)
                code = wireCode.GetString()!;
            if (json.RootElement.TryGetProperty("message", out var wireMessage) && wireMessage.ValueKind == JsonValueKind.String)
                message = wireMessage.GetString();
            if (json.RootElement.TryGetProperty("retryable", out var wireRetry) && wireRetry.ValueKind is JsonValueKind.True or JsonValueKind.False)
                retryable = wireRetry.GetBoolean();
        }
        catch (JsonException) { /* HTTP status remains authoritative for non-JSON proxy errors. */ }
        throw new FederationQueryException(code, (int)response.StatusCode, message, retryable, nodeId: session.NodeId);
    }

    private async Task<ReadOnlyMemory<byte>> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxResponseBytes)
            throw new FederationQueryException("InvalidPeerResponse", 502, "The node response exceeds the byte limit.", nodeId: session.NodeId);
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                // Keep the owned buffer instead of doubling the response allocation with ToArray().
                bytes.TryGetBuffer(out var segment);
                return segment.AsMemory();
            }
            // The stream is measured after any content decoding performed by the transport.
            if (bytes.Length + read > MaxResponseBytes)
                throw new FederationQueryException("InvalidPeerResponse", 502, "The decoded node response exceeds the byte limit.", nodeId: session.NodeId);
            bytes.Write(buffer, 0, read);
        }
    }
}

public sealed class PeerSearchTargetResolver(INodeIdentityProvider identity, LocalSearchSnapshotService snapshots,
    IPeerSessionFactory sessions, INodeTransport transport) : IPeerSearchTargetResolver
{
    public const string LocalGrantId = "local-library";

    public async Task<PeerSearchTarget> ResolveAsync(string nodeId, CancellationToken cancellationToken)
    {
        var local = await identity.GetAsync(cancellationToken);
        if (local.NodeId == nodeId)
            return new(local.NodeId, local.LibraryEpoch, new LocalPeerSearchClient(snapshots, LocalGrantId));
        PeerSessionSnapshot session;
        try { session = await sessions.GetAsync(nodeId, cancellationToken); }
        catch (FederationAccessException exception)
        {
            throw new FederationQueryException(exception.ErrorCode, exception.StatusCode, exception.Message,
                nodeId: nodeId, innerException: exception);
        }
        var info = session.Info;
        if (info.QueryContractVersion != 1 || info.SupportedFilters == null || info.SupportedSorts == null ||
            !new[] { "text", "fileAvailability", "sourceKinds" }.All(info.SupportedFilters.Contains) ||
            !new[] { "NameAsc", "NameDesc" }.All(info.SupportedSorts.Contains) || info.MaxBatchSize < 1)
            throw new FederationQueryException("ProtocolIncompatible", 422, nodeId: nodeId);
        return new(session.NodeId, session.LibraryEpoch, new HttpPeerSearchClient(transport, session));
    }
}
