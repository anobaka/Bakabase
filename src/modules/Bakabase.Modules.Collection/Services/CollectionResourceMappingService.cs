using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Collection.Services;

/// <summary>
/// Memberships, held in memory both ways round.
/// <para>
/// The cache is the ORM's, not this class's: a second copy would be one more thing to keep in step
/// with the database. Both directions are derived from that one cache on each call, which is cheap
/// at the scale of "how many collections does one library have".
/// </para>
/// </summary>
public class CollectionResourceMappingService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, CollectionResourceMappingDbModel, int> orm)
    : ICollectionResourceMappingService
    where TDbContext : DbContext
{
    public async Task<List<CollectionMember>> GetByCollectionId(int collectionId) =>
        (await orm.GetAll(x => x.CollectionId == collectionId))
        .OrderBy(x => x.Order ?? int.MaxValue)
        .ThenBy(x => x.Id)
        .Select(x => x.ToDomainModel())
        .ToList();

    public async Task<List<CollectionMember>> GetByResourceId(int resourceId) =>
        (await orm.GetAll(x => x.ResourceId == resourceId))
        .Select(x => x.ToDomainModel())
        .ToList();

    public async Task<Dictionary<int, List<int>>> GetCollectionIdsByResourceIds(
        IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count == 0) return [];

        var wanted = resourceIds.ToHashSet();

        return (await orm.GetAll(x => wanted.Contains(x.ResourceId)))
            .GroupBy(x => x.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionId).Distinct().ToList());
    }

    public async Task<List<int>> GetResourceIds(int collectionId) =>
        (await GetByCollectionId(collectionId)).Select(m => m.ResourceId).ToList();

    public async Task<int> Add(int collectionId, IReadOnlyCollection<int> resourceIds,
        CollectionMembershipOrigin origin = CollectionMembershipOrigin.Manual,
        int? subscriptionId = null, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return 0;

        var existing = (await orm.GetAll(x => x.CollectionId == collectionId))
            .Select(x => x.ResourceId)
            .ToHashSet();

        // Adding something twice must not reset the fact that it was ignored, or lose its place.
        var toAdd = resourceIds.Distinct().Where(id => !existing.Contains(id)).ToList();

        if (toAdd.Count == 0) return 0;

        var now = DateTime.Now;

        await orm.AddRange(toAdd.Select(id => new CollectionResourceMappingDbModel
        {
            CollectionId = collectionId,
            ResourceId = id,
            Origin = origin,
            SubscriptionId = subscriptionId,
            LastSeenAt = origin == CollectionMembershipOrigin.Subscription ? now : null,
            CreatedAt = now,
        }).ToList());

        return toAdd.Count;
    }

    public async Task Remove(int collectionId, IReadOnlyCollection<int> resourceIds,
        CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return;

        var wanted = resourceIds.ToHashSet();
        var rows = await orm.GetAll(x => x.CollectionId == collectionId && wanted.Contains(x.ResourceId));

        if (rows.Count > 0) await orm.RemoveRange(rows);
    }

    public async Task SetIgnored(int collectionId, int resourceId, bool ignored,
        CancellationToken ct = default)
    {
        var row = (await orm.GetAll(x => x.CollectionId == collectionId && x.ResourceId == resourceId))
            .FirstOrDefault();

        if (row == null || row.IsIgnored == ignored) return;

        row.IsIgnored = ignored;
        await orm.Update(row);
    }

    public async Task Reorder(int collectionId, IReadOnlyList<int> resourceIdsInOrder,
        CancellationToken ct = default)
    {
        var rows = await orm.GetAll(x => x.CollectionId == collectionId);
        var byResource = rows.ToDictionary(x => x.ResourceId);
        var changed = new List<CollectionResourceMappingDbModel>();

        for (var i = 0; i < resourceIdsInOrder.Count; i++)
        {
            if (!byResource.TryGetValue(resourceIdsInOrder[i], out var row) || row.Order == i) continue;

            row.Order = i;
            changed.Add(row);
        }

        if (changed.Count > 0) await orm.UpdateRange(changed);
    }

    public async Task MarkSeen(int collectionId, IReadOnlyCollection<int> resourceIds, DateTime seenAt,
        CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return;

        var wanted = resourceIds.ToHashSet();
        var rows = (await orm.GetAll(x => x.CollectionId == collectionId && wanted.Contains(x.ResourceId)))
            .ToList();

        foreach (var row in rows) row.LastSeenAt = seenAt;

        if (rows.Count > 0) await orm.UpdateRange(rows);
    }

    public async Task RemoveByCollectionId(int collectionId, CancellationToken ct = default)
    {
        var rows = await orm.GetAll(x => x.CollectionId == collectionId);

        if (rows.Count > 0) await orm.RemoveRange(rows);
    }

    public async Task RemoveByResourceIds(IReadOnlyCollection<int> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return;

        var wanted = resourceIds.ToHashSet();
        var rows = await orm.GetAll(x => wanted.Contains(x.ResourceId));

        if (rows.Count > 0) await orm.RemoveRange(rows);
    }
}
