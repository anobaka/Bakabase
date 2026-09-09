using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Services;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// The collection service with the two things the module deliberately does not know: how to run a
/// resource search, and what is currently being acquired.
/// <para>
/// Keeping those out of the module is what lets it be a module at all — it would otherwise depend
/// on the search evaluator and the acquisition module, and be neither testable nor optional.
/// </para>
/// </summary>
public class BakabaseCollectionService(
    FullMemoryCacheResourceService<BakabaseDbContext, CollectionDbModel, int> orm,
    ICollectionResourceMappingService mappings,
    IResourceService resources,
    IResourceDataChangeEventPublisher changePublisher,
    IResourceProfileService profiles,
    IAcquisitionService acquisitions,
    ILogger<BakabaseCollectionService> logger)
    : CollectionService<BakabaseDbContext>(orm, mappings, resources, changePublisher)
{
    /// <summary>
    /// Cached because the list page asks for every collection's progress at once, and a rule
    /// evaluation is a full search. Invalidated by resource changes rather than by a timer: the
    /// answer is only ever wrong because a resource changed.
    /// </summary>
    private static readonly Dictionary<int, (HashSet<int> Ids, long Generation)> RuleCache = new();

    private static long _generation;

    /// <summary>Called when resources change, which is the only thing that can move a rule's answer.</summary>
    public static void InvalidateRuleCache() => Interlocked.Increment(ref _generation);

    protected override async Task<IReadOnlyList<int>> RuleMatchedResourceIds(int collectionId,
        CancellationToken ct)
    {
        var collection = await Orm.GetByKey(collectionId, false);

        if (collection?.RuleSearchJson is not {Length: > 0} rule) return [];

        var generation = Interlocked.Read(ref _generation);

        lock (RuleCache)
        {
            if (RuleCache.TryGetValue(collectionId, out var cached) && cached.Generation == generation)
            {
                return cached.Ids.ToList();
            }
        }

        HashSet<int> matched;
        try
        {
            matched = await profiles.GetMatchingResourceIdsBySearchJson(rule);
        }
        catch (Exception ex)
        {
            // A rule the user has half-written should leave the collection showing its written-down
            // members, not an error page.
            logger.LogWarning(ex, "[Collection] Could not evaluate the rule of collection {Id}", collectionId);

            return [];
        }

        lock (RuleCache)
        {
            RuleCache[collectionId] = (matched, generation);
        }

        return matched.ToList();
    }

    protected override async Task<HashSet<int>> AcquiringResourceIds(IReadOnlyCollection<int> resourceIds,
        CancellationToken ct)
    {
        if (resourceIds.Count == 0) return [];

        var wanted = resourceIds.ToHashSet();
        var live = new HashSet<int>();

        foreach (var status in new[]
                 {
                     AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting
                 })
        {
            foreach (var task in await acquisitions.SearchAsync(status, ct: ct))
            {
                if (wanted.Contains(task.ResourceId)) live.Add(task.ResourceId);
            }
        }

        return live;
    }
}
