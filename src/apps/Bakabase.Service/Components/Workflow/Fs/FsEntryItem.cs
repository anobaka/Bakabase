using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Fs;

/// <summary>
/// One filesystem entry flowing through a cleaning chain. The core invariant of the design
/// (docs/file-cleaning-workflow.html §2): every text change on the chain touches only
/// <see cref="WorkingName"/>; the disk is touched by nothing but the saveName activity — which
/// in the preview phase only records a plan. "Compute the new name" and "write it to disk" are
/// therefore two phases by construction, and preview costs nothing extra.
/// </summary>
public sealed record FsEntryItem
{
    /// <summary>Full path as it is on disk right now.</summary>
    public required string Path { get; init; }

    public required bool IsDirectory { get; init; }

    /// <summary>Name when the item entered the chain (with extension for files).</summary>
    public required string OriginalName { get; init; }

    /// <summary>The name being worked on. Transforms replace the item with a copy carrying a
    /// new value here; comparing it against <see cref="OriginalName"/> is how saveName knows
    /// whether there is anything to do.</summary>
    public required string WorkingName { get; init; }
}

public class FsEntryItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.FsEntry;
    public string DisplayName => "Filesystem entry";
    public System.Type ClrType => typeof(FsEntryItem);
}
