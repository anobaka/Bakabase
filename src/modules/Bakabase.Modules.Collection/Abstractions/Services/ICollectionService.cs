using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Models.Input;

namespace Bakabase.Modules.Collection.Abstractions.Services;

/// <summary>One page of a collection's members, with the ids the caller then loads resources for.</summary>
public record CollectionMemberPage(
    IReadOnlyList<int> ResourceIds,
    int TotalCount,
    int PageIndex,
    int PageSize);

public interface ICollectionService
{
    Task<List<ResourceCollection>> GetAll(bool withProgress = false, CancellationToken ct = default);

    Task<ResourceCollection?> Get(int id, bool withProgress = false, CancellationToken ct = default);

    Task<ResourceCollection> Add(CollectionInputModel input, CancellationToken ct = default);

    Task<ResourceCollection> Put(int id, CollectionInputModel input, CancellationToken ct = default);

    Task Delete(int id, CancellationToken ct = default);

    /// <summary>Every member, written-down and rule-matched alike, deduplicated.</summary>
    Task<List<CollectionMember>> GetMembers(int id, CancellationToken ct = default);

    Task<CollectionMemberPage> SearchMembers(int id, CollectionMemberFilter filter, int pageIndex,
        int pageSize, CancellationToken ct = default);

    /// <summary>How much of it is here. Ignored members are outside the ratio entirely.</summary>
    Task<CollectionProgress> GetProgress(int id, CancellationToken ct = default);

    Task<Dictionary<int, CollectionProgress>> GetProgressMany(IReadOnlyCollection<int> ids,
        CancellationToken ct = default);

    /// <summary>Adds resources and tells everything that watches resources that they changed.</summary>
    Task AddMembers(int id, IReadOnlyCollection<int> resourceIds,
        CollectionMembershipOrigin origin = CollectionMembershipOrigin.Manual,
        int? subscriptionId = null, CancellationToken ct = default);

    Task RemoveMembers(int id, IReadOnlyCollection<int> resourceIds, CancellationToken ct = default);

    Task SetMemberIgnored(int id, int resourceId, bool ignored, CancellationToken ct = default);

    Task ReorderMembers(int id, IReadOnlyList<int> resourceIdsInOrder, CancellationToken ct = default);
}
