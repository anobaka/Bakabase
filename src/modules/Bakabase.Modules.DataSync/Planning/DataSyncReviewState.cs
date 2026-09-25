using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// One live incoming entity of a review, with its display position and item id.
/// </summary>
/// <param name="Keys">The record's keys, primary first; null when one of them is not a sync key.</param>
/// <param name="ItemId"><c>"{kind}/k/{primaryKey}"</c>, or <c>"{kind}/p/{position}"</c> when the keys are unreadable.</param>
internal sealed record DataSyncReviewIncoming(DataSyncIncomingEntity Entity, int Position, string ItemId,
    IReadOnlyList<SyncKey>? Keys)
{
    /// <summary>
    /// The live records of <paramref name="staged"/> in display order: the peer's shared order
    /// (<see cref="DataSyncOrderPlanner.Compare"/>) for kinds with order, else the order they arrived in. Tombstones
    /// take no part in a review, which never removes anything (§2.1).
    /// </summary>
    public static IReadOnlyList<DataSyncReviewIncoming> Of(DataSyncStagedKind staged, IDataSyncKindCodec? codec)
    {
        ArgumentNullException.ThrowIfNull(staged);
        var live = staged.Entities.Select((entity, index) => (Entity: entity, Index: index))
            .Where(x => !x.Entity.Record.Deleted).ToList();
        if (codec?.Descriptor.HasOrder == true)
        {
            var entries = live.ToDictionary(x => x.Index, x => new DataSyncOrderEntry("", x.Entity.Record.OrderKey,
                x.Entity.Record.Keys.Count == 0 ? "" : DataSyncOrderPlanner.TieKeyOf(x.Entity.Record.Keys)));
            live.Sort((a, b) =>
            {
                var byOrder = DataSyncOrderPlanner.Compare(entries[a.Index], entries[b.Index]);
                return byOrder != 0 ? byOrder : a.Index.CompareTo(b.Index);
            });
        }

        var result = new List<DataSyncReviewIncoming>(live.Count);
        for (var position = 0; position < live.Count; position++)
        {
            var entity = live[position].Entity;
            var keys = entity.Record.Keys.Count > 0 && entity.Record.Keys.All(SyncKey.IsValid)
                ? entity.Record.Keys.Select(k => new SyncKey(k)).ToList()
                : null;
            var itemId = keys is null
                ? $"{staged.Kind}/p/{position.ToString(CultureInfo.InvariantCulture)}"
                : $"{staged.Kind}/k/{keys[0].Value}";
            result.Add(new DataSyncReviewIncoming(entity, position, itemId, keys));
        }

        return result;
    }
}

/// <summary>
/// This device's side of one kind for a review (v3.1 §5.2 step 1, §5.2 here): the Synced candidates, the key index
/// over them, the keys of rows kept out of sync, tombstoned keys and version vectors.
/// </summary>
internal sealed class DataSyncReviewLocal
{
    private readonly Dictionary<SyncKey, List<LocalIdentifiedEntity>> _syncedByKey = new();
    private readonly Dictionary<string, LocalIdentifiedEntity> _byLocalKey = new(StringComparer.Ordinal);
    private readonly HashSet<SyncKey> _notSyncedKeys = [];
    private readonly Dictionary<string, DataSyncVersionVector> _vvs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LocalIdentifiedEntity>> _byName;

    /// <param name="snapshot">The Synced entities (<c>ToPlannerSnapshot</c>); null = none.</param>
    /// <param name="state">
    /// Every live row with its vector and state; null when the caller has none, which only loses the §8.3
    /// refinement and the keys of rows kept out of sync.
    /// </param>
    /// <param name="codec">Indexes names for natural matching; null for a kind this build does not know.</param>
    public DataSyncReviewLocal(LocalKindSnapshot? snapshot, DataSyncLocalKindState? state, IDataSyncKindCodec? codec)
    {
        Entities = (snapshot?.Entities ?? []).OrderBy(e => e.Position).ThenBy(e => e.LocalKey, LocalKeyComparer.Instance)
            .ToList();
        TombstonedKeys = snapshot?.TombstonedKeys ?? new HashSet<SyncKey>();
        foreach (var entity in Entities)
        {
            _byLocalKey.TryAdd(entity.LocalKey, entity);
            foreach (var key in entity.Keys.All)
            {
                if (!_syncedByKey.TryGetValue(key, out var list)) _syncedByKey[key] = list = [];
                if (!list.Any(e => ReferenceEquals(e, entity))) list.Add(entity);
            }
        }

        foreach (var row in state?.Entities ?? [])
        {
            _vvs.TryAdd(row.LocalKey, row.Vv);
            if (row.State != DataSyncEntitySyncState.Synced) _notSyncedKeys.UnionWith(row.Keys.All);
        }

        // Every level a natural match can reach (Clash and above) needs the same name ignoring case and surrounding
        // whitespace (DataSyncNaturalMatch), so only locals of that name are asked.
        _byName = new Dictionary<string, List<LocalIdentifiedEntity>>(StringComparer.OrdinalIgnoreCase);
        if (codec is null) return;
        foreach (var entity in Entities)
        {
            var name = codec.NameOf(entity.Content).Trim();
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = [];
            list.Add(entity);
        }
    }

    /// <summary>Synced live entities in local order.</summary>
    public IReadOnlyList<LocalIdentifiedEntity> Entities { get; }

    public IReadOnlySet<SyncKey> TombstonedKeys { get; }

    public LocalIdentifiedEntity? ByLocalKey(string? localKey) =>
        localKey is not null && _byLocalKey.TryGetValue(localKey, out var entity) ? entity : null;

    /// <summary>The distinct Synced entities any of <paramref name="keys"/> is bound to, in local order.</summary>
    public IReadOnlyList<LocalIdentifiedEntity> Matches(IEnumerable<SyncKey> keys)
    {
        var matches = new List<LocalIdentifiedEntity>();
        foreach (var key in keys)
        {
            if (!_syncedByKey.TryGetValue(key, out var list)) continue;
            foreach (var entity in list)
            {
                if (!matches.Any(e => ReferenceEquals(e, entity))) matches.Add(entity);
            }
        }

        return matches.OrderBy(e => e.Position).ThenBy(e => e.LocalKey, LocalKeyComparer.Instance).ToList();
    }

    /// <summary>Locals that may match <paramref name="name"/> naturally: the same name ignoring case and trim.</summary>
    public IReadOnlyList<LocalIdentifiedEntity> NamedLike(string name) =>
        _byName.TryGetValue(name.Trim(), out var list) ? list : [];

    /// <summary>
    /// A key of a LocalOnly or Detached row: the user chose not to sync that entity, so the record is ignored
    /// (§5.2, row X).
    /// </summary>
    public bool IsNotSyncedHere(IEnumerable<SyncKey> keys) => keys.Any(_notSyncedKeys.Contains);

    /// <summary>A key some live row here owns: Synced, LocalOnly or Detached (§5.3).</summary>
    public bool IsBound(SyncKey key) => _syncedByKey.ContainsKey(key) || _notSyncedKeys.Contains(key);

    public DataSyncVersionVector? VvOf(string localKey) => _vvs.GetValueOrDefault(localKey);

    /// <summary>
    /// v3.1 §7.7's alias rule: the incoming keys that are neither keys of <paramref name="target"/> nor keys of
    /// another live entity (LocalOnly and Detached rows included, §5.3), which the identity store would refuse.
    /// </summary>
    public IReadOnlyList<SyncKey> AliasKeysFor(IEnumerable<SyncKey> incomingKeys, LocalIdentifiedEntity target) =>
        incomingKeys.Where(k => !target.Keys.Contains(k) && !_notSyncedKeys.Contains(k) &&
                                (!_syncedByKey.TryGetValue(k, out var owners) ||
                                 owners.All(o => o.LocalKey == target.LocalKey)))
            .Distinct().ToList();
}

/// <summary>Local keys are integer ids as invariant strings: numeric order first, ordinal for anything else.</summary>
internal sealed class LocalKeyComparer : IComparer<string>
{
    public static LocalKeyComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var xNumber = long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var a);
        var yNumber = long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var b);
        if (xNumber && yNumber && a != b) return a.CompareTo(b);
        if (xNumber != yNumber) return xNumber ? -1 : 1;
        return string.CompareOrdinal(x, y);
    }
}
