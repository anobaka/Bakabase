using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Input;

/// <summary>
/// Request model for previewing path mark matched paths
/// </summary>
public class PathMarkPreviewRequest
{
    /// <summary>
    /// Root path to preview
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// Mark type
    /// </summary>
    public PathMarkType Type { get; set; }

    /// <summary>
    /// Config JSON (ResourceMarkConfig or PropertyMarkConfig)
    /// </summary>
    public string ConfigJson { get; set; } = null!;
}
