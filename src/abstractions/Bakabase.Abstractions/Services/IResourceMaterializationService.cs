namespace Bakabase.Abstractions.Services;

/// <summary>
/// Knobs for a single materialization. The defaults are what an interactive "the files are here
/// now" action wants.
/// </summary>
/// <param name="MergeIfPathOwnedByAnotherResource">
/// What to do when the target path is already owned by another resource — the common case being a
/// path-mark sync that discovered the folder before the user linked it to the resource they had
/// been tracking. When true the two are merged, with the resource being materialized surviving;
/// when false the materialization fails instead.
/// </param>
/// <param name="EnqueuePathMarkSync">
/// Whether to ask <see cref="IPathMarkSyncService"/> to run right away. Marking the covering marks
/// as pending always happens; this only decides whether the sync is kicked immediately or left for
/// the next scheduled pass.
/// </param>
public record MaterializationOptions(
    bool MergeIfPathOwnedByAnotherResource = true,
    bool EnqueuePathMarkSync = true)
{
    public static MaterializationOptions Default { get; } = new();
}

/// <summary>
/// What a materialization did, beyond the resource now having a path.
/// </summary>
/// <param name="ResourceId">The resource that now owns the path — always the one passed in.</param>
/// <param name="Path">The standardized path that was written.</param>
/// <param name="MergedResourceId">
/// The resource that used to own the path and was absorbed, or null when the path was free.
/// </param>
public record MaterializationResult(int ResourceId, string Path, int? MergedResourceId)
{
    public bool Merged => MergedResourceId.HasValue;
}

/// <summary>
/// The single entry point for a resource gaining or losing its local files.
/// <para>
/// A resource without a path is not a broken resource: it is one Bakabase knows about but that is
/// not materialized on disk yet — an uninstalled Steam game, a DLsite work that has not been
/// downloaded, a work the user intends to acquire. Materializing it is more than writing
/// <c>Resource.Path</c>: file times have to be read, a resource that already occupies the path has
/// to be absorbed, the filesystem caches are stale, parent-child relationships change, path marks
/// covering the new path must re-sync, and enhancers that gave up because there were no files must
/// be allowed to run again. Every one of those was previously the caller's job to remember; this
/// service exists so there is exactly one place that gets it right.
/// </para>
/// </summary>
public interface IResourceMaterializationService
{
    /// <summary>
    /// Points <paramref name="resourceId"/> at <paramref name="path"/> and applies every side
    /// effect of that transition. Idempotent: materializing a resource to the path it already has
    /// refreshes the derived state and is otherwise harmless.
    /// </summary>
    /// <exception cref="ArgumentException">The path does not exist on disk.</exception>
    /// <exception cref="InvalidOperationException">
    /// The resource does not exist, or the path belongs to another resource and
    /// <see cref="MaterializationOptions.MergeIfPathOwnedByAnotherResource"/> is false.
    /// </exception>
    Task<MaterializationResult> MaterializeAsync(int resourceId, string path,
        MaterializationOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Clears the resource's path, keeping its identity, properties and name. The resource stays
    /// searchable and keeps its place in collections — it simply has no local files any more.
    /// </summary>
    Task DematerializeAsync(int resourceId, CancellationToken ct = default);
}
