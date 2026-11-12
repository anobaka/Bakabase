namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>The producer of an acquisition task, not a URL from which its files are downloaded.</summary>
public sealed record AcquisitionCreationOrigin
{
    /// <summary>postParser, nameSearch, manual, module or unknown.</summary>
    public string Kind { get; init; } = "unknown";
    public string? Module { get; init; }
    public string? EntityKind { get; init; }
    public string? EntityId { get; init; }
    public string? Revision { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? BuilderId { get; init; }
    /// <summary>available, deleted, missing, snapshot or unknown; never authorizes execution.</summary>
    public string SourceState { get; init; } = "unknown";
}
