using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Workflow.Artifacts;

public sealed class ArtifactOperationDbModel
{
    [Key] public string Id { get; set; } = "";
    public string IntentKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string InputFingerprint { get; set; } = "";
    public bool Restartable { get; set; }
    public ArtifactOperationState State { get; set; } = ArtifactOperationState.Planned;
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public string? OutputArtifactId { get; set; }
    public string? Error { get; set; }
    public int Attempt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ArtifactDbModel
{
    [Key] public string Id { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string Kind { get; set; } = "files";
    /// <summary>AppData-relative when RootIsAppData; absolute for user-selected storage.</summary>
    public string RootPath { get; set; } = "";
    public bool RootIsAppData { get; set; }
    /// <summary>The previous file locations remain fenced while a quarantine is in progress or retained.</summary>
    public string? QuarantineSourceRootPath { get; set; }
    public bool QuarantineSourceRootIsAppData { get; set; }
    public string ManifestJson { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public ArtifactLocationState State { get; set; } = ArtifactLocationState.Available;
    public bool Managed { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ArtifactConsumerDbModel
{
    [Key] public string Id { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string ConsumerKey { get; set; } = "";
    public string? OwnerKind { get; set; }
    public string? OwnerId { get; set; }
    public DateTime LeaseUntil { get; set; }
    public DateTime? ReleasedAt { get; set; }
}

public sealed class ArtifactLineageDbModel
{
    [Key] public string Id { get; set; } = "";
    public string ParentArtifactId { get; set; } = "";
    public string ChildArtifactId { get; set; } = "";
}
