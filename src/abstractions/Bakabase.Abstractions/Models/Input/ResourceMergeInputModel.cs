namespace Bakabase.Abstractions.Models.Input;

public record ResourceMergeInputModel
{
    /// <summary>
    /// The target resource ID that will survive the merge.
    /// All source links and media library mappings will be consolidated to this resource.
    /// </summary>
    public int TargetResourceId { get; set; }

    /// <summary>
    /// Resource IDs to be merged into the target (source links + media library mappings transferred, then deleted).
    /// </summary>
    public int[] SourceResourceIds { get; set; } = [];

    /// <summary>
    /// Resource IDs to be deleted without merging any data.
    /// </summary>
    public int[] DeleteResourceIds { get; set; } = [];
}
