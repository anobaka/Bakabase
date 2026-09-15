namespace Bakabase.Abstractions.Services;

/// <summary>What a collection is called, for anything that needs to name one.</summary>
public record CollectionName(int Id, string Name);

/// <summary>
/// The names of the collections, and nothing else.
/// <para>
/// It exists so the property system can offer "which collection" as a choice without depending on
/// the collection module — the same arrangement media libraries have. Anything that needs more than
/// a name should ask the collection service.
/// </para>
/// </summary>
public interface ICollectionNameProvider
{
    Task<List<CollectionName>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Which collections each resource belongs to — written-down memberships and rule matches
    /// alike. Resources in none are absent from the result.
    /// </summary>
    Task<Dictionary<int, List<CollectionName>>> GetByResourceIdsAsync(
        IReadOnlyCollection<int> resourceIds, CancellationToken ct = default);
}
