using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Input;

namespace Bakabase.Modules.Player.Abstractions.Services;

/// <summary>
/// Opens a multi-resource selection with one external player in a single
/// launch (playlist file or multi-file command line).
/// </summary>
public interface IBatchPlayService
{
    /// <summary>
    /// Players the given selection can be opened with: players configured in
    /// the resources' profiles first, then known players discovered on this
    /// machine.
    /// </summary>
    Task<List<BatchPlayCandidate>> GetCandidatesAsync(int[] resourceIds, CancellationToken ct);

    /// <summary>
    /// Collects the playable files of the selection and launches the chosen
    /// player once with all of them. Throws <see cref="InvalidOperationException"/>
    /// with a user-facing message when the request cannot be fulfilled.
    /// </summary>
    Task<BatchPlayResult> PlayAsync(BatchPlayInputModel input, CancellationToken ct);

    /// <summary>
    /// Players a saved playlist can be opened with, each annotated with how
    /// many of the playlist's files it can handle
    /// (<see cref="BatchPlayCandidate.MatchedFileCount"/>).
    /// </summary>
    Task<List<BatchPlayCandidate>> GetPlaylistCandidatesAsync(int playlistId, CancellationToken ct);

    /// <summary>
    /// Opens a saved playlist with the chosen player in one launch, keeping
    /// playlist order and including only files the player supports.
    /// </summary>
    Task<BatchPlayResult> PlayPlaylistAsync(int playlistId, string playerKey, CancellationToken ct);
}
