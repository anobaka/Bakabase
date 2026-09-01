using System.Collections.Generic;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Fs;

/// <summary>
/// One filesystem entry flowing through a cleaning chain. The core invariant of the design
/// (docs/file-cleaning-workflow.html §2): every text change on the chain touches only
/// <see cref="WorkingName"/>; the disk is touched by nothing but the saveName activity — which
/// in the preview phase only records a plan. "Compute the new name" and "write it to disk" are
/// therefore two phases by construction, and preview costs nothing extra.
/// </summary>
public sealed record FsEntryItem : ITextWorkpiece, IHasWorkflowSystemVariables
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

    /// <summary>The text-family contract (capability map E3): the working text of a filesystem
    /// entry IS its working name, so every <c>transform.text.*</c> activity applies here.</summary>
    public string WorkingText => WorkingName;

    public object WithWorkingText(string workingText) => this with {WorkingName = workingText};

    /// <summary>
    /// The fs domain's system variables (capability map E4): stable facts of the entry, taken
    /// from the ORIGINAL disk state — a chain that mangles the working name must still be able
    /// to rebuild "{var:title}.{var:extension}" from truth. Directories have an empty extension.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetWorkflowSystemVariables() => new Dictionary<string, string>
    {
        ["extension"] = IsDirectory ? "" : System.IO.Path.GetExtension(OriginalName).TrimStart('.'),
        ["originalName"] = OriginalName,
        ["parentName"] = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? "") ?? "",
    };
}

public class FsEntryItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.FsEntry;
    public string DisplayName => "Filesystem entry";
    public System.Type ClrType => typeof(FsEntryItem);
}
