using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>Current stage locations, independent of immutable attempt outputs. Historical bindings
/// point at recorded locations; displaying them never moves files or grants deletion rights.</summary>
public sealed record AcquisitionStageDirectories
{
    public int Version { get; init; } = 1;
    public int TaskId { get; init; }
    public Dictionary<string, AcquisitionPartDirectories> Parts { get; init; } = [];
}

public sealed record AcquisitionPartDirectories
{
    public AcquisitionStageDirectory? Download { get; init; }
    public AcquisitionStageDirectory? Processing { get; init; }
    public AcquisitionStageDirectory? Delivery { get; init; }
}

public sealed record AcquisitionStageDirectory(string Directory, string Ownership, string Evidence)
{
    /// <summary>Present only for new, explicitly allocated task storage. Borrowed historical
    /// paths must not be promoted into exclusive ownership from a directory name.</summary>
    public ArtifactDirectoryScope? Allocation { get; init; }
}
