using System.Text.Json.Serialization;

namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Information extracted from a post. An empty list means no download links were found.</summary>
public record PostDownloadInfo
{
    public int SchemaVersion { get; init; } = 2;
    public bool IsComplete { get; init; } = true;
    public List<string> Warnings { get; init; } = [];
    public PostAvailabilityAssessment? Availability { get; init; }
    public string? Title { get; init; }
    public List<PostDownloadResource> Resources { get; init; } = [];
}

/// <summary>A download location and its credentials, without any download or library policy.</summary>
public record PostDownloadResource
{
    public string Link { get; init; } = "";
    public string? Code { get; init; }
    public string? Password { get; init; }
    public PostExtractionPlan? Extraction { get; init; }
    public PostLinkHealth? LinkHealth { get; init; }
}

/// <summary>Ordered post-download file processing. The Extraction naming remains for serialized compatibility.</summary>
public record PostExtractionPlan
{
    public string Requirement { get; init; } = "unknown";
    public List<PostExtractionStep> Steps { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

public record PostExtractionStep
{
    public string Id { get; init; } = "";
    public string Op { get; init; } = "";
    public string Input { get; init; } = "download";
    public string? Selector { get; init; }
    public string? Extension { get; init; }
    public string? Password { get; init; }
    /// <summary>renameFile: a file name only, retained in the input file's relative directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }
    /// <summary>moveFile: a relative output directory; the input file name is preserved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDirectory { get; init; }
}

public record PostAvailabilityAssessment
{
    public string Status { get; init; } = "unknown";
    public List<string> Evidence { get; init; } = [];
    public string? Reason { get; init; }
}

/// <summary>Observed link health, separate from statements made by authors or commenters.</summary>
public record PostLinkHealth
{
    public string Status { get; init; } = "unknown";
    public string? Reason { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
