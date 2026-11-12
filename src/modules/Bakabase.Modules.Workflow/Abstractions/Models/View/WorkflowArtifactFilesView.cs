using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

/// <summary>Recorded exact manifest, not a filesystem scan or a fresh integrity check.</summary>
public sealed record WorkflowArtifactFilesView(string ArtifactId, string State, int FileCount,
    long TotalBytes, int PageIndex, int PageSize, IReadOnlyList<ArtifactManifestEntry> Entries)
{
    /// <summary>Current registered root, rebased to this server. Historical node outputs may contain an old root.</summary>
    public string? Directory { get; init; }
}

public sealed record WorkflowArtifactDirectoryEntry(string RelativePath, bool IsDirectory, long? Length);
public sealed record WorkflowArtifactDirectoryView(string ArtifactId, string State, string RelativeDirectory,
    int FileCount, long TotalBytes, int ChildCount, int PageIndex, int PageSize,
    IReadOnlyList<WorkflowArtifactDirectoryEntry> Entries);
