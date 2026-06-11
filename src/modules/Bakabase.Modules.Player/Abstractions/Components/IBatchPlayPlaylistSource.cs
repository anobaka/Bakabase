namespace Bakabase.Modules.Player.Abstractions.Components;

/// <summary>
/// One playable entry of a playlist, in playlist order.
/// </summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="ResourceId">Backing resource id when the entry came from a resource item.</param>
public record BatchPlayPlaylistEntry(string Path, int? ResourceId);

/// <summary>
/// Resolved view of a playlist for batch play.
/// </summary>
public record BatchPlayPlaylistSnapshot(string Name, List<BatchPlayPlaylistEntry> Entries);

/// <summary>
/// Port through which the player module reads playlists. The playlist domain
/// lives in the legacy business layer, which modules cannot reference; the
/// host registers an adapter. Resource items are expected to be resolved to
/// their playable files (profile-filtered), keeping playlist batch play
/// consistent with resource batch play.
/// </summary>
public interface IBatchPlayPlaylistSource
{
    /// <summary>
    /// Returns the playlist's playable entries in order, or null when the
    /// playlist does not exist.
    /// </summary>
    Task<BatchPlayPlaylistSnapshot?> GetSnapshotAsync(int playlistId, CancellationToken ct);
}
