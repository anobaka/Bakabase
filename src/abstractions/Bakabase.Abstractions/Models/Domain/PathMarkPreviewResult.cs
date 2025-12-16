namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Result for a single matched path in preview
/// </summary>
public class PathMarkPreviewResult
{
    /// <summary>
    /// Matched path
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// For Resource type: the layer index (0-based) of the resource segment in the path
    /// For example, if root is "/a/b" and matched path is "/a/b/c/d", and c is the resource, this would be 2
    /// </summary>
    public int? ResourceLayerIndex { get; set; }

    /// <summary>
    /// For Resource type: the segment name at the resource layer
    /// </summary>
    public string? ResourceSegmentName { get; set; }

    /// <summary>
    /// For Property type: the extracted property value
    /// </summary>
    public string? PropertyValue { get; set; }
}
