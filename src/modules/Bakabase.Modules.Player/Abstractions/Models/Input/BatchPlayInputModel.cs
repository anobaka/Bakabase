using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Player.Abstractions.Models.Input;

public record BatchPlayInputModel
{
    public required int[] ResourceIds { get; init; }

    /// <summary>
    /// <see cref="Domain.BatchPlayCandidate.Key"/> of the player to use.
    /// </summary>
    public required string PlayerKey { get; init; }

    public BatchPlayFileSelectionMode FileSelectionMode { get; init; } =
        BatchPlayFileSelectionMode.FirstFilePerResource;
}

public record BatchPlayCandidatesInputModel
{
    public required int[] ResourceIds { get; init; }
}

public record PlaylistBatchPlayInputModel
{
    /// <summary>
    /// <see cref="Domain.BatchPlayCandidate.Key"/> of the player to use.
    /// </summary>
    public required string PlayerKey { get; init; }
}
