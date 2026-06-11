using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Abstractions.Models.Domain;

public record BatchPlayResult
{
    public required string PlayerName { get; init; }
    public BatchPlayLaunchMethod LaunchMethod { get; init; }

    /// <summary>
    /// Resources that contributed at least one file.
    /// </summary>
    public int ResourceCount { get; init; }

    public int FileCount { get; init; }

    public List<BatchPlaySkippedResource> SkippedResources { get; init; } = [];

    /// <summary>
    /// Files that were selected but no longer exist on disk.
    /// </summary>
    public int MissingFileCount { get; init; }
}

public record BatchPlaySkippedResource(int ResourceId, BatchPlaySkipReason Reason);

public enum BatchPlaySkipReason
{
    NoPlayableFiles = 1,
    AllFilesMissing = 2,
    ResourceNotFound = 3,

    /// <summary>
    /// The resource has playable files, but none with an extension the
    /// chosen player supports.
    /// </summary>
    NoFilesMatchingPlayer = 4,
}
