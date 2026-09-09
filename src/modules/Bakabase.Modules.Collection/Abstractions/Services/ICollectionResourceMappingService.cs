using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Collection.Abstractions.Services;

/// <summary>
/// The written-down memberships, both ways round.
/// <para>
/// Both directions are asked for constantly and by different things — a collection page wants its
/// members, and every resource card wants its collections — so both are indexed rather than one
/// being a scan over the other.
/// </para>
/// </summary>
public interface ICollectionResourceMappingService
{
    Task<List<CollectionMember>> GetByCollectionId(int collectionId);

    Task<List<CollectionMember>> GetByResourceId(int resourceId);

    /// <summary>Which collections each of these resources is written into. Resources with none are absent.</summary>
    Task<Dictionary<int, List<int>>> GetCollectionIdsByResourceIds(IReadOnlyCollection<int> resourceIds);

    /// <summary>Every resource written into this collection, in the order the user gave them.</summary>
    Task<List<int>> GetResourceIds(int collectionId);

    /// <summary>
    /// Adds resources to a collection. Already-present resources are left exactly as they are —
    /// adding something twice must not reset the fact that it was ignored, or lose its place.
    /// </summary>
    Task<int> Add(int collectionId, IReadOnlyCollection<int> resourceIds,
        CollectionMembershipOrigin origin = CollectionMembershipOrigin.Manual,
        int? subscriptionId = null, CancellationToken ct = default);

    Task Remove(int collectionId, IReadOnlyCollection<int> resourceIds, CancellationToken ct = default);

    Task SetIgnored(int collectionId, int resourceId, bool ignored, CancellationToken ct = default);

    /// <summary>Writes the given order onto the members named, in the order given.</summary>
    Task Reorder(int collectionId, IReadOnlyList<int> resourceIdsInOrder, CancellationToken ct = default);

    /// <summary>Records that a source still lists these, so a member that vanished can be shown as such.</summary>
    Task MarkSeen(int collectionId, IReadOnlyCollection<int> resourceIds, DateTime seenAt,
        CancellationToken ct = default);

    Task RemoveByCollectionId(int collectionId, CancellationToken ct = default);

    Task RemoveByResourceIds(IReadOnlyCollection<int> resourceIds, CancellationToken ct = default);
}
