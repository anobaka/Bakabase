using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Search;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// What each collection's rule currently matches, and which collections a resource is in.
/// <para>
/// Evaluating a rule is a full resource search, and the collections list asks every collection for
/// its progress at once — so a page of twenty rule collections would otherwise be twenty searches.
/// The reverse direction cannot be computed at all without this: "which collections is this
/// resource in" would be one search per rule collection, per batch of resources, every time the
/// search index is built.
/// </para>
/// <para>
/// The storage is the same <see cref="SearchSetIndex"/> the profile index uses. What differs is the
/// staleness rule, and it differs on purpose: a profile index may serve a slightly old answer while
/// a background task catches up, but a user who has just edited a property expects the collection to
/// have noticed. So a resource change makes every answer stale at once, and a stale answer is
/// re-evaluated when it is next asked for rather than in the background.
/// </para>
/// </summary>
public class CollectionRuleIndex(ILogger<CollectionRuleIndex> logger)
{
    private readonly SearchSetIndex _index = new();

    /// <summary>Which generation each collection's stored answer was computed in.</summary>
    private readonly ConcurrentDictionary<int, long> _evaluatedAt = new();

    private long _generation;

    /// <summary>
    /// Read before evaluating and passed back when storing, so an answer computed across a change
    /// is recognised as already stale rather than stored as current.
    /// </summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>A resource changed, which is the only thing that can move a rule's answer.</summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    /// <summary>The collection is gone, or has stopped being a rule collection.</summary>
    public void Forget(int collectionId)
    {
        _evaluatedAt.TryRemove(collectionId, out _);
        _index.Remove(collectionId);
    }

    /// <summary>
    /// What this collection's rule matches, evaluating it if the stored answer is from before the
    /// last resource change.
    /// </summary>
    public async Task<IReadOnlyList<int>> MembersAsync(int collectionId, string? ruleSearchJson,
        IResourceProfileService profiles, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ruleSearchJson))
        {
            Forget(collectionId);

            return [];
        }

        await EnsureCurrent(collectionId, ruleSearchJson, profiles, ct);

        return _index.GetMembers(collectionId).ToList();
    }

    /// <summary>
    /// Brings every one of these collections up to date, so the reverse direction can be read.
    /// </summary>
    public async Task EnsureCurrentAsync(IEnumerable<(int Id, string? RuleSearchJson)> collections,
        IResourceProfileService profiles, CancellationToken ct = default)
    {
        foreach (var (id, rule) in collections)
        {
            if (string.IsNullOrEmpty(rule))
            {
                Forget(id);

                continue;
            }

            await EnsureCurrent(id, rule, profiles, ct);
        }
    }

    /// <summary>
    /// Which rule collections this resource is matched by. Only meaningful once
    /// <see cref="EnsureCurrentAsync"/> has been given every rule collection there is — this reads
    /// what is stored and never evaluates, because it is called per resource in batches.
    /// </summary>
    public IReadOnlyList<int> CollectionIdsOf(int resourceId) => _index.GetSetIds(resourceId);

    private async Task EnsureCurrent(int collectionId, string ruleSearchJson,
        IResourceProfileService profiles, CancellationToken ct)
    {
        var generation = Generation;

        if (_evaluatedAt.TryGetValue(collectionId, out var evaluated) && evaluated == generation) return;

        HashSet<int> matched;
        try
        {
            matched = await profiles.GetMatchingResourceIdsBySearchJson(ruleSearchJson);
        }
        catch (Exception ex)
        {
            // A rule the user has half-written should leave the collection showing its written-down
            // members, not an error page. Deliberately not stored: the next read tries again, so
            // finishing the rule fixes it without anything having to be invalidated.
            logger.LogWarning(ex, "[Collection] Could not evaluate the rule of collection {Id}", collectionId);

            return;
        }

        ct.ThrowIfCancellationRequested();

        _index.Replace(collectionId, matched);

        // A change that landed while the search was running has already made this answer stale;
        // recording it as current would cache something known to be out of date.
        if (Generation == generation) _evaluatedAt[collectionId] = generation;
    }
}
