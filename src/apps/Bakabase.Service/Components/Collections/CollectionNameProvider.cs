using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Services;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// The names of the collections, for the parts of the app that need to name one without knowing
/// what a collection is — the property system's choices, the search index, the legacy search.
/// </summary>
public class CollectionNameProvider(
    ICollectionService collections,
    ICollectionResourceMappingService mappings,
    CollectionRuleIndex ruleIndex,
    IResourceProfileService profiles) : ICollectionNameProvider
{
    public async Task<List<CollectionName>> GetAllAsync(CancellationToken ct = default) =>
        (await collections.GetAll(false, ct)).Select(c => new CollectionName(c.Id, c.Name)).ToList();

    public async Task<Dictionary<int, List<CollectionName>>> GetByResourceIdsAsync(
        IReadOnlyCollection<int> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0) return [];

        var all = await collections.GetAll(false, ct);
        var byId = all.ToDictionary(c => c.Id, c => new CollectionName(c.Id, c.Name));
        var result = new Dictionary<int, List<CollectionName>>();

        void Attach(int resourceId, int collectionId)
        {
            if (!byId.TryGetValue(collectionId, out var name)) return;

            if (!result.TryGetValue(resourceId, out var list))
            {
                result[resourceId] = list = [];
            }

            if (list.All(x => x.Id != collectionId)) list.Add(name);
        }

        foreach (var (resourceId, collectionIds) in await mappings.GetCollectionIdsByResourceIds(resourceIds))
        {
            foreach (var collectionId in collectionIds) Attach(resourceId, collectionId);
        }

        // Rule members too: membership is membership, and a search that only saw written-down rows
        // would disagree with the collection page about who is in it.
        //
        // Read out of the index rather than by asking each collection for its members: this runs per
        // batch while the search index is built, and the answer for one resource is a dictionary
        // lookup once every rule has been evaluated for this generation.
        var rules = all.Where(c => c.HasRule).Select(c => (c.Id, c.RuleSearchJson)).ToList();

        if (rules.Count == 0) return result;

        await ruleIndex.EnsureCurrentAsync(rules, profiles, ct);

        foreach (var resourceId in resourceIds)
        {
            foreach (var collectionId in ruleIndex.CollectionIdsOf(resourceId))
            {
                Attach(resourceId, collectionId);
            }
        }

        return result;
    }
}
