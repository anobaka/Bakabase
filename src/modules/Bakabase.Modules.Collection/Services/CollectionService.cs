using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Collection.Services;

public class CollectionService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, CollectionDbModel, int> orm,
    ICollectionResourceMappingService mappings,
    IResourceService resources,
    IResourceDataChangeEventPublisher changePublisher)
    : ICollectionService
    where TDbContext : DbContext
{
    /// <summary>Exposed so a derived service can read a collection's rule without a second lookup.</summary>
    protected FullMemoryCacheResourceService<TDbContext, CollectionDbModel, int> Orm => orm;

    public async Task<List<ResourceCollection>> GetAll(bool withProgress = false, CancellationToken ct = default)
    {
        var all = (await orm.GetAll())
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => c.ToDomainModel())
            .ToList();

        if (withProgress && all.Count > 0)
        {
            var progress = await GetProgressMany(all.Select(c => c.Id).ToList(), ct);

            foreach (var c in all) c.Progress = progress.GetValueOrDefault(c.Id);
        }

        return all;
    }

    public async Task<ResourceCollection?> Get(int id, bool withProgress = false, CancellationToken ct = default)
    {
        var row = await orm.GetByKey(id, false);

        if (row == null) return null;

        var domain = row.ToDomainModel();

        if (withProgress) domain.Progress = await GetProgress(id, ct);

        return domain;
    }

    public async Task<ResourceCollection> Add(CollectionInputModel input, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var created = await orm.Add(new CollectionDbModel
        {
            Name = input.Name,
            Description = input.Description,
            Color = input.Color,
            CoverPath = input.CoverPath,
            RuleSearchJson = input.RuleSearchJson,
            AutoAcquire = input.AutoAcquire,
            AcquisitionSettingsJson = input.AcquisitionSettingsJson,
            Order = input.Order,
            CreatedAt = now,
            UpdatedAt = now,
        });

        return created.Data!.ToDomainModel();
    }

    public async Task<ResourceCollection> Put(int id, CollectionInputModel input, CancellationToken ct = default)
    {
        var row = await orm.GetByKey(id, false)
                  ?? throw new InvalidOperationException($"Collection #{id} does not exist.");

        var ruleChanged = row.RuleSearchJson != input.RuleSearchJson;

        row.Name = input.Name;
        row.Description = input.Description;
        row.Color = input.Color;
        row.CoverPath = input.CoverPath;
        row.RuleSearchJson = input.RuleSearchJson;
        row.AutoAcquire = input.AutoAcquire;
        row.AcquisitionSettingsJson = input.AcquisitionSettingsJson;
        row.Order = input.Order;
        row.UpdatedAt = DateTime.Now;

        await orm.Update(row);

        if (ruleChanged)
        {
            // Changing the rule changes who is in the collection, which changes what every affected
            // resource's collection property says. Nothing else would notice.
            await PublishMembersChanged(await MemberResourceIds(id, ct));
        }

        return row.ToDomainModel();
    }

    public async Task Delete(int id, CancellationToken ct = default)
    {
        var affected = await MemberResourceIds(id, ct);

        await mappings.RemoveByCollectionId(id, ct);
        await orm.RemoveByKey(id);
        await PublishMembersChanged(affected);
    }

    public async Task<List<CollectionMember>> GetMembers(int id, CancellationToken ct = default)
    {
        var written = await mappings.GetByCollectionId(id);
        var seen = written.Select(m => m.ResourceId).ToHashSet();
        var members = new List<CollectionMember>(written);

        foreach (var resourceId in await RuleMatchedResourceIds(id, ct))
        {
            // A resource that is both written down and matched by the rule appears once, and keeps
            // the state the written-down row carries.
            if (seen.Add(resourceId))
            {
                members.Add(new CollectionMember {CollectionId = id, ResourceId = resourceId});
            }
        }

        return members;
    }

    public async Task<CollectionMemberPage> SearchMembers(int id, CollectionMemberFilter filter,
        int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var members = await GetMembers(id, ct);
        var ids = members.Select(m => m.ResourceId).ToArray();
        var byId = await LoadResources(ids);
        var acquiring = await AcquiringResourceIds(ids, ct);

        var matching = members.Where(m => Matches(m, byId, acquiring, filter)).ToList();
        var page = Math.Max(1, pageIndex);
        var size = Math.Clamp(pageSize, 1, 500);

        return new CollectionMemberPage(
            matching.Skip((page - 1) * size).Take(size).Select(m => m.ResourceId).ToList(),
            matching.Count, page, size);
    }

    public async Task<CollectionProgress> GetProgress(int id, CancellationToken ct = default) =>
        (await GetProgressMany([id], ct)).GetValueOrDefault(id) ?? new CollectionProgress(0, 0, 0, 0);

    public async Task<Dictionary<int, CollectionProgress>> GetProgressMany(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<int, CollectionProgress>();

        if (ids.Count == 0) return result;

        // Loaded once for every collection asked about: the list page asks for all of them, and a
        // query per collection would be a query per card.
        var membersByCollection = new Dictionary<int, List<CollectionMember>>();

        foreach (var id in ids) membersByCollection[id] = await GetMembers(id, ct);

        var allResourceIds = membersByCollection.Values
            .SelectMany(m => m.Select(x => x.ResourceId))
            .Distinct()
            .ToArray();
        var byId = await LoadResources(allResourceIds);
        var acquiring = await AcquiringResourceIds(allResourceIds, ct);

        foreach (var (id, members) in membersByCollection)
        {
            var ignored = members.Count(m => m.IsIgnored);
            // Ignored members are outside the ratio entirely — neither had nor missing. That is
            // what keeps a rate honest when a series includes three drama CDs nobody wants.
            var counted = members.Where(m => !m.IsIgnored).ToList();
            var owned = counted.Count(m => byId.GetValueOrDefault(m.ResourceId)?.HasLocalPath == true);
            var beingAcquired = counted.Count(m =>
                byId.GetValueOrDefault(m.ResourceId)?.HasLocalPath != true &&
                acquiring.Contains(m.ResourceId));

            result[id] = new CollectionProgress(counted.Count, owned, beingAcquired, ignored);
        }

        return result;
    }

    public async Task AddMembers(int id, IReadOnlyCollection<int> resourceIds,
        CollectionMembershipOrigin origin = CollectionMembershipOrigin.Manual,
        int? subscriptionId = null, CancellationToken ct = default)
    {
        var added = await mappings.Add(id, resourceIds, origin, subscriptionId, ct);

        if (added > 0) await PublishMembersChanged(resourceIds);
    }

    public async Task RemoveMembers(int id, IReadOnlyCollection<int> resourceIds,
        CancellationToken ct = default)
    {
        await mappings.Remove(id, resourceIds, ct);
        await PublishMembersChanged(resourceIds);
    }

    public async Task SetMemberIgnored(int id, int resourceId, bool ignored, CancellationToken ct = default)
    {
        await mappings.SetIgnored(id, resourceId, ignored, ct);
        // Ignoring changes the collected ratio but not which collections the resource is in, so the
        // resource itself is unchanged — only the collection's own numbers move.
    }

    public Task ReorderMembers(int id, IReadOnlyList<int> resourceIdsInOrder, CancellationToken ct = default) =>
        mappings.Reorder(id, resourceIdsInOrder, ct);

    // ------- helpers -------

    private async Task<List<int>> MemberResourceIds(int id, CancellationToken ct) =>
        (await GetMembers(id, ct)).Select(m => m.ResourceId).ToList();

    /// <summary>
    /// Resources the collection's rule matches. Overridden in the app layer, where the search
    /// evaluator lives; the module by itself has no way to run a resource search.
    /// </summary>
    protected virtual Task<IReadOnlyList<int>> RuleMatchedResourceIds(int collectionId,
        CancellationToken ct) => Task.FromResult<IReadOnlyList<int>>([]);

    /// <summary>
    /// Which of these are being acquired. Also overridden in the app layer — the module does not
    /// depend on the acquisition module, and does not need to in order to be useful.
    /// </summary>
    protected virtual Task<HashSet<int>> AcquiringResourceIds(IReadOnlyCollection<int> resourceIds,
        CancellationToken ct) => Task.FromResult(new HashSet<int>());

    private async Task<Dictionary<int, Bakabase.Abstractions.Models.Domain.Resource>> LoadResources(
        int[] ids)
    {
        if (ids.Length == 0) return [];

        return (await resources.GetAll(r => ids.Contains(r.Id))).ToDictionary(r => r.Id);
    }

    private static bool Matches(CollectionMember member,
        Dictionary<int, Bakabase.Abstractions.Models.Domain.Resource> byId,
        HashSet<int> acquiring, CollectionMemberFilter filter)
    {
        var owned = byId.GetValueOrDefault(member.ResourceId)?.HasLocalPath == true;

        return filter switch
        {
            CollectionMemberFilter.All => true,
            CollectionMemberFilter.Owned => owned,
            CollectionMemberFilter.Missing => !owned && !acquiring.Contains(member.ResourceId) &&
                                              !member.IsIgnored,
            CollectionMemberFilter.Acquiring => !owned && acquiring.Contains(member.ResourceId),
            CollectionMemberFilter.Ignored => member.IsIgnored,
            // A member a source brought in and has since stopped listing. Written-down only: a rule
            // match has no source to have stopped listing it.
            CollectionMemberFilter.GoneFromSource => member.Origin == CollectionMembershipOrigin.Subscription &&
                                                     member.LastSeenAt != null &&
                                                     member.LastSeenAt < DateTime.Now.AddDays(-7),
            _ => true
        };
    }

    /// <summary>
    /// Membership is part of what a resource is, as far as search and the cards are concerned, so a
    /// change to it has to be announced the same way a property change would be.
    /// </summary>
    private Task PublishMembersChanged(IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count > 0) changePublisher.PublishResourcesChanged(resourceIds.ToArray());

        return Task.CompletedTask;
    }
}
