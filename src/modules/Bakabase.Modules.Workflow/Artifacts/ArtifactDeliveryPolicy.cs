namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>Publication-only path preferences. Never changes a processing checkpoint or its files.</summary>
public sealed record ArtifactDeliveryPolicy
{
    public int Version { get; init; } = 1;
    public bool RemoveGeneratedDirectories { get; init; } = true;
    /// <summary>Remove at most one remaining common outer directory; retain its internal grouping.</summary>
    public bool CollapseSingleRootDirectory { get; init; }
}

/// <summary>Measured executor evidence. Ancestors are exact relative directory paths, never name patterns.</summary>
public sealed record ArtifactDirectoryEntry(string RelativePath, bool IsDirectory,
    IReadOnlyList<string> GeneratedAncestors);

/// <summary>Missing legacy evidence grants no directory-removal authority.</summary>
public sealed record ArtifactDirectoryProvenance(int Version, string ManifestFingerprint,
    IReadOnlyList<ArtifactDirectoryEntry> Entries);

public sealed record ArtifactDeliveryPathMapping(string SourceRelativePath, string TargetRelativePath);

public sealed record ArtifactDeliveryLayoutPreview(string InputArtifactId, string InputFingerprint,
    ArtifactDeliveryPolicy? Policy, bool Changed, IReadOnlyList<ArtifactDeliveryPathMapping> FileMappings,
    IReadOnlyList<ArtifactDeliveryPathMapping> DirectoryMappings, IReadOnlyList<string> RemovedDirectories,
    ArtifactManifest OutputManifest, string ProjectionFingerprint,
    IReadOnlyList<string>? RemovedGeneratedDirectories = null, string? CollapsedAuthorRootDirectory = null);

/// <summary>Committed publication projection bound to the exact source and output manifests.</summary>
public sealed record ArtifactDeliveryLayoutProof(int Version, string InputArtifactId, string InputFingerprint,
    ArtifactDeliveryPolicy Policy, string OutputFingerprint, string ProjectionFingerprint,
    IReadOnlyList<ArtifactDeliveryPathMapping> FileMappings, IReadOnlyList<ArtifactDeliveryPathMapping> DirectoryMappings);
