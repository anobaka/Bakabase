namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>A caller-owned local allocation. The configured storage root must already exist;
/// the executor creates the exact child directory only after persisting its ownership receipt.</summary>
public sealed record ArtifactDirectoryScope(string OwnerKind, string OwnerId, string Role,
    string StorageRoot, string Directory);
