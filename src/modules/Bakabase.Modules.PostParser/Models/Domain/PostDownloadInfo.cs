using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Information extracted from a post. An empty list means no download links were found.</summary>
public record PostDownloadInfo
{
    public int SchemaVersion { get; init; } = 3;
    public bool IsComplete { get; init; } = true;
    public List<string> Warnings { get; init; } = [];
    public PostAvailabilityAssessment? Availability { get; init; }
    public PostContentReadinessAssessment? ContentReadiness { get; init; }
    public string? Title { get; init; }
    public List<PostDownloadGroup> Groups { get; init; } = [];
    public List<PostDownloadResource> Resources { get; init; } = [];
    /// <summary>Presentation candidates only; never download resources or archive inputs.</summary>
    public List<PostCoverCandidate> CoverCandidates { get; init; } = [];
}

public sealed record PostCoverCandidate
{
    public string Url { get; init; } = "";
    public string Kind { get; init; } = "preview";
    public List<string> Evidence { get; init; } = [];
}

/// <summary>Read-only compatibility view. Exclusions retain original resource indices and source identities.</summary>
public sealed record PostDownloadMediaView
{
    public List<PostCoverCandidate> CoverCandidates { get; init; } = [];
    public List<int> ExcludedResourceIndices { get; init; } = [];
}

/// <summary>AI-assessed content identity and purpose. Members are alternatives for the same content, not required parts.</summary>
public record PostDownloadGroup
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "unknown";
    public string? Summary { get; init; }
    public List<string> Evidence { get; init; } = [];
}

/// <summary>A download location and its credentials, without any download or library policy.</summary>
public record PostDownloadResource
{
    public string Link { get; init; } = "";
    public string? GroupId { get; init; }
    public string? Code { get; init; }
    public string? Password { get; init; }
    public PostExtractionPlan? Extraction { get; init; }
    public PostLinkHealth? LinkHealth { get; init; }
}

/// <summary>Ordered post-download file processing. The Extraction naming remains for serialized compatibility.</summary>
public record PostExtractionPlan : FileProcessingPlan;

public record PostExtractionStep : FileProcessingStep;

public record PostAvailabilityAssessment
{
    public string Status { get; init; } = "unknown";
    public List<string> Evidence { get; init; } = [];
    public string? Reason { get; init; }
    public PostContentReadinessAssessment? ContentReadiness { get; init; }
}

/// <summary>Observed link health, separate from statements made by authors or commenters.</summary>
public record PostLinkHealth
{
    public string? ReasonCode { get; init; }
    public string? ProbeId { get; init; }
    public int? ProbeVersion { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public Dictionary<string, string>? Evidence { get; init; }
    public string Status { get; init; } = "unknown";
    public string? Reason { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
