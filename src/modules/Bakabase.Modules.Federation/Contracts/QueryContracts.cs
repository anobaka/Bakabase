using System.Text.Json.Serialization;

namespace Bakabase.Modules.Federation.Contracts;

/// <summary>An identity in one incarnation of one owner's library, never a path or an external work ID.</summary>
public sealed record ResourceRef(string NodeId, string LibraryEpoch, int ResourceId);

/// <summary>Captured unknown properties are rejected by the protocol validator, with either serializer.</summary>
public abstract record StrictQueryInput
{
    [JsonExtensionData]
    [Newtonsoft.Json.JsonExtensionData]
    public Dictionary<string, object?>? AdditionalFields { get; init; }
}

public sealed record CommonLibraryQuery : StrictQueryInput
{
    public int QueryContractVersion { get; init; } = 1;
    public string? Text { get; init; }
    public string? FileAvailability { get; init; } = "Any";
    public int[]? SourceKinds { get; init; }
    public string Sort { get; init; } = "NameAsc";
}

public sealed record LocalFederatedQuery : StrictQueryInput
{
    public string[] NodeIds { get; init; } = [];
    public CommonLibraryQuery Query { get; init; } = new();
    public int PageSize { get; init; } = 50;
}

/// <summary>The export contract deliberately has no node list, URL or hop count.</summary>
public sealed record NodeExportQuery : StrictQueryInput
{
    public string ExpectedLibraryEpoch { get; init; } = "";
    public CommonLibraryQuery Query { get; init; } = new();
    public int BlockSize { get; init; } = 128;
}

public sealed record FederatedResourceSummary
{
    public required ResourceRef Ref { get; init; }
    public string OwnerLabel { get; init; } = "";
    public string? Title { get; init; }
    public string DisplayName { get; init; } = "";
    public string? FileName { get; init; }
    public int[] SourceKinds { get; init; } = [];
    public string FileAvailability { get; init; } = "MetadataOnly";
    public string? NormalizedSortKey { get; init; }
    public string? CoverAsset { get; init; }
    public string[] PlaybackCapabilities { get; init; } = [];
}

public sealed record NodeQueryBlock
{
    public required string SnapshotId { get; init; }
    public required string NodeId { get; init; }
    public required string LibraryEpoch { get; init; }
    public required string QueryHash { get; init; }
    public long TotalCount { get; init; }
    public int Offset { get; init; }
    public FederatedResourceSummary[] Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public long ExpiresInMs { get; init; }
    public DateTimeOffset CaptureStartedAt { get; init; }
    public DateTimeOffset CaptureCompletedAt { get; init; }
    public string Consistency { get; init; } = "frozen-observation-v1";
}

public sealed record QueryParticipant(string NodeId, string LibraryEpoch, long TotalCount);
public sealed record QueryOmittedNode(string NodeId, string Code, bool Retryable);

public sealed record FederatedQueryPage
{
    public required string SessionId { get; init; }
    public FederatedResourceSummary[] Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public long ExpiresInMs { get; init; }
    public QueryParticipant[] Participants { get; init; } = [];
    public QueryOmittedNode[] OmittedNodes { get; init; } = [];
    public long TotalWithinParticipants { get; init; }
    public bool CoverageComplete { get; init; }
}

public sealed class FederationQueryException : Exception
{
    public FederationQueryException(string code, int statusCode, string? message = null,
        bool retryable = false, string? field = null, string? nodeId = null,
        IReadOnlyList<QueryOmittedNode>? omittedNodes = null, Exception? innerException = null)
        : base(message ?? code, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Retryable = retryable;
        Field = field;
        NodeId = nodeId;
        OmittedNodes = omittedNodes;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public bool Retryable { get; }
    public string? Field { get; }
    public string? NodeId { get; }
    public IReadOnlyList<QueryOmittedNode>? OmittedNodes { get; }
}
