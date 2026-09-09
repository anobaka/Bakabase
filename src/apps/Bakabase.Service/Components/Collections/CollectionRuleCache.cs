using System.Collections.Generic;
using System.Threading;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// What each collection's rule last matched, remembered until a resource changes.
/// <para>
/// Evaluating a rule is a full resource search, and the collections list asks every collection for
/// its progress at once — so a page of twenty rule collections would otherwise be twenty searches.
/// </para>
/// </summary>
public class CollectionRuleCache
{
    private readonly Dictionary<int, (HashSet<int> Ids, long Generation)> _entries = new();

    private long _generation;

    /// <summary>
    /// Read before evaluating and passed back to <see cref="Set"/>, so an answer computed across a
    /// change is recognised as already stale rather than stored as current.
    /// </summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>A resource changed, which is the only thing that can move a rule's answer.</summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    public HashSet<int>? Get(int collectionId, long generation)
    {
        lock (_entries)
        {
            return _entries.TryGetValue(collectionId, out var entry) && entry.Generation == generation
                ? entry.Ids
                : null;
        }
    }

    public void Set(int collectionId, HashSet<int> ids, long generation)
    {
        lock (_entries)
        {
            // A change that landed while the search was running has already invalidated this
            // answer; storing it would be caching something known to be out of date.
            if (Generation != generation) return;

            _entries[collectionId] = (ids, generation);
        }
    }
}
