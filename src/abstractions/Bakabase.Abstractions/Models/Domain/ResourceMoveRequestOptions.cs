namespace Bakabase.Abstractions.Models.Domain;

public record ResourceMoveRequestOptions
{
    public string? Origin { get; set; }
    public string? SourceTabId { get; set; }
    public string? SourceTabName { get; set; }
    public string? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ExpectedPreviewFingerprint { get; set; }
    /// <summary>inherit, ask, or overwrite. Inherit only applies to move-panel requests.</summary>
    public string ConflictPolicy { get; set; } = "inherit";
}

public record ResourceMoveConflictResolution
{
    public string Action { get; set; } = "overwrite";
    public string Scope { get; set; } = "once";
    public int ConflictVersion { get; set; }
}
