using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Abstractions.Components;

/// <param name="Files">Its FileSystem playable files, in order; empty when it has none.</param>
public record BatchPlayResourceFiles(int ResourceId, List<string> Files);

/// <summary>
/// Everything the batch-play orchestration needs to know about a selection of resources.
/// </summary>
/// <param name="Resources">
/// Only the resources that exist, in the order they were asked for. One the caller named
/// that is absent here is a resource that is gone.
/// </param>
/// <param name="ConfiguredPlayers">
/// The players the matching profiles configure, deduplicated by executable path and in
/// profile order. They are an explicit per-source choice, which is why they outrank the
/// players merely found installed.
/// </param>
public record BatchPlayResourceSnapshot(
    List<BatchPlayResourceFiles> Resources,
    List<MediaLibraryPlayer> ConfiguredPlayers);

/// <summary>
/// Port through which the player module reads resources.
/// </summary>
/// <remarks>
/// It exists because the machine holding the library is not always the machine starting
/// the player. The host registers an adapter: on the all-in-one one that reads the
/// database directly, in a relay for a managed server one that asks that server. Everything
/// downstream of this — which player, which files, how to launch it — is then the same
/// code in both.
/// </remarks>
public interface IBatchPlayResourceSource
{
    /// <summary>
    /// Reads the selection in one go. One call rather than a question per resource,
    /// because through a relay each one is a round trip.
    /// </summary>
    Task<BatchPlayResourceSnapshot> GetSnapshotAsync(int[] resourceIds, CancellationToken ct);

    /// <summary>
    /// Records what was played, one entry per resource. Best effort by contract: the
    /// player is already running by the time this is called.
    /// </summary>
    Task MarkPlayedAsync(IReadOnlyDictionary<int, string> playedByResourceId, CancellationToken ct);
}
