using System.Collections.Concurrent;

namespace Bakabase.Abstractions.Components.Search;

/// <summary>
/// Which resources each saved search matches, and — the direction nobody can compute on demand —
/// which saved searches a resource is in.
/// <para>
/// Two things in this codebase are the same idea: a resource profile is a search with configuration
/// hanging off it, and a rule collection is a search with a name on it. Both are asked the same two
/// questions constantly and by different callers: a profile page wants its resources, and every
/// resource card wants its profiles. Only the first can be answered by running the search; the
/// second is a scan over every search there is unless it is kept.
/// </para>
/// <para>
/// This holds both directions and nothing else. It runs no searches, knows nothing about profiles
/// or collections, and takes no opinion on when an answer has gone stale — its owner decides that,
/// because the two owners decide it differently.
/// </para>
/// </summary>
public sealed class SearchSetIndex
{
    private readonly ConcurrentDictionary<int, IReadOnlySet<int>> _members = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<int>> _setsByResource = new();
    private readonly ConcurrentDictionary<int, int> _ranks = new();

    /// <summary>
    /// Both directions move together or a reader sees a resource in a set the set does not have.
    /// Held only across the bookkeeping, never across a search.
    /// </summary>
    private readonly Lock _gate = new();

    private volatile bool _isReady;
    private TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether the first full build has finished. Until then, answers are not answers.</summary>
    public bool IsReady => _isReady;

    /// <summary>The sets currently indexed.</summary>
    public IReadOnlyCollection<int> SetIds => _members.Keys.ToList();

    public async Task WaitUntilReady(CancellationToken ct = default)
    {
        if (_isReady) return;

        // A copy, because a rebuild replaces the field and the waiter must keep the one it started
        // waiting on.
        var tcs = _readyTcs;

        await using (ct.Register(() => tcs.TrySetCanceled()))
        {
            await tcs.Task;
        }
    }

    public void MarkReady()
    {
        _isReady = true;
        _readyTcs.TrySetResult();
    }

    /// <summary>Empties the index and makes it not ready — the start of a full rebuild.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _isReady = false;

            // Anyone already waiting keeps waiting on the same completion; a fresh one is only
            // needed once the old one has been used up.
            if (_readyTcs.Task.IsCompleted)
            {
                _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _members.Clear();
            _setsByResource.Clear();
            _ranks.Clear();
        }
    }

    /// <summary>
    /// How a set sorts against the others a resource is in. Highest first — for profiles this is
    /// the priority that decides which one's configuration wins.
    /// </summary>
    public void SetRank(int setId, int rank)
    {
        lock (_gate)
        {
            _ranks[setId] = rank;

            // Every list holding this set is now sorted by a stale rank.
            foreach (var resourceId in _setsByResource.Keys)
            {
                if (_setsByResource.TryGetValue(resourceId, out var sets) && sets.Contains(setId))
                {
                    _setsByResource[resourceId] = Sorted(sets);
                }
            }
        }
    }

    public IReadOnlySet<int> GetMembers(int setId) =>
        _members.TryGetValue(setId, out var members) ? members : EmptySet;

    /// <summary>The sets this resource is in, highest-ranked first.</summary>
    public IReadOnlyList<int> GetSetIds(int resourceId) =>
        _setsByResource.TryGetValue(resourceId, out var sets) ? sets : [];

    /// <summary>
    /// Records what a set now matches.
    /// </summary>
    /// <returns>
    /// The resources whose membership actually changed — joined or left. That is what callers need:
    /// a resource whose set membership did not move needs nothing recomputed about it, and telling
    /// them otherwise turns every rebuild into a cache flush.
    /// </returns>
    public IReadOnlySet<int> Replace(int setId, IEnumerable<int> members)
    {
        var next = members as IReadOnlySet<int> ?? members.ToHashSet();

        lock (_gate)
        {
            var previous = GetMembers(setId);
            var changed = new HashSet<int>();

            foreach (var resourceId in next)
            {
                if (!previous.Contains(resourceId)) changed.Add(resourceId);
            }

            foreach (var resourceId in previous)
            {
                if (!next.Contains(resourceId)) changed.Add(resourceId);
            }

            // Stored even when nothing moved, so a set that matches nothing is still a set the
            // index knows about rather than one it has never heard of.
            _members[setId] = next;

            foreach (var resourceId in changed)
            {
                Rewrite(resourceId, setId, next.Contains(resourceId));
            }

            return changed;
        }
    }

    /// <summary>Forgets a set entirely — it was deleted, or is being rebuilt from nothing.</summary>
    /// <returns>The resources that were in it.</returns>
    public IReadOnlySet<int> Remove(int setId)
    {
        lock (_gate)
        {
            if (!_members.TryRemove(setId, out var previous)) return EmptySet;

            _ranks.TryRemove(setId, out _);

            foreach (var resourceId in previous)
            {
                Rewrite(resourceId, setId, false);
            }

            return previous;
        }
    }

    /// <summary>Drops these resources from every set — they no longer exist.</summary>
    public void Forget(IEnumerable<int> resourceIds)
    {
        lock (_gate)
        {
            foreach (var resourceId in resourceIds)
            {
                if (!_setsByResource.TryRemove(resourceId, out var sets)) continue;

                foreach (var setId in sets)
                {
                    if (_members.TryGetValue(setId, out var members) && members.Contains(resourceId))
                    {
                        _members[setId] = members.Where(x => x != resourceId).ToHashSet();
                    }
                }
            }
        }
    }

    /// <summary>Adds or removes one set from one resource's list. Caller holds the gate.</summary>
    private void Rewrite(int resourceId, int setId, bool belongs)
    {
        var current = GetSetIds(resourceId);

        if (belongs)
        {
            if (current.Contains(setId)) return;

            _setsByResource[resourceId] = Sorted(current.Append(setId));

            return;
        }

        var without = current.Where(x => x != setId).ToList();

        // An empty list and an absent one mean the same thing, so only one of them is kept.
        if (without.Count == 0)
        {
            _setsByResource.TryRemove(resourceId, out _);
        }
        else
        {
            _setsByResource[resourceId] = without;
        }
    }

    /// <summary>
    /// Highest rank first, and the set id breaks a tie — so two sets of equal priority always come
    /// back in the same order rather than in whichever order they were indexed.
    /// </summary>
    private List<int> Sorted(IEnumerable<int> setIds) =>
        setIds.OrderByDescending(id => _ranks.GetValueOrDefault(id))
            .ThenBy(id => id)
            .ToList();

    private static readonly IReadOnlySet<int> EmptySet = new HashSet<int>();
}
