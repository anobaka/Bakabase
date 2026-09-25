using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// What the identity operations of one apply changed (§5.3; v3.1 §8.6 extended): the apply runner puts it into the
/// history entry's pre-image. Undo in continuous sync never deletes a side row or an alias (§8.11); what it gives
/// back is the owner of every key a key move changed (<see cref="DataSyncIdentityStore.RestoreKeyOwnersAsync"/>).
/// </summary>
public sealed class DataSyncIdentityPreImage
{
    /// <summary>Tombstones a create revived, as they were (v3.1 M-a).</summary>
    public List<DataSyncEntityDbModel> RevivedTombstones { get; init; } = [];

    /// <summary>Tombstoned rows an alias-add retired, as they were (N5).</summary>
    public List<DataSyncEntityDbModel> RetiredIdentities { get; init; } = [];

    /// <summary>Aliases of a retired row, now pointing at the entity that absorbed it (N5).</summary>
    public List<DataSyncAliasRepoint> RepointedAliases { get; init; } = [];

    /// <summary>Alias rows of tombstones deleted so their key could be used again.</summary>
    public List<DataSyncAliasRow> RemovedAliases { get; init; } = [];

    public List<DataSyncAliasRow> AddedAliases { get; init; } = [];

    /// <summary>Keys that changed owner by a rekey, a KeepWithEntity or a KeepRecordLinked (§9.2), in order.</summary>
    public List<DataSyncKeyMove> KeyMoves { get; init; } = [];

    public bool IsEmpty => RevivedTombstones.Count == 0 && RetiredIdentities.Count == 0 &&
                           RepointedAliases.Count == 0 && RemovedAliases.Count == 0 && AddedAliases.Count == 0 &&
                           KeyMoves.Count == 0;
}

public sealed record DataSyncAliasRow(string Kind, string AliasKey, string SyncKey);

public sealed record DataSyncAliasRepoint(string Kind, string AliasKey, string FromSyncKey, string ToSyncKey);

/// <summary>Who owned a key: an entity row (by its Id, which survives rekeys) as its primary or as an alias.</summary>
public sealed record DataSyncKeyOwner(int EntityId, bool Primary);

/// <summary>One key changing owner; a null owner means no row held the key.</summary>
public sealed record DataSyncKeyMove(string Kind, string Key, DataSyncKeyOwner? Before, DataSyncKeyOwner? After);

/// <summary>What a tombstone records (§6.3). The vector comes from DataSyncRevisionRules and never goes backwards.</summary>
public sealed record DataSyncTombstoneWrite(DataSyncVersionVector Vv, DataSyncTombstoneKind TombstoneKind, bool Served,
    DataSyncEditorRef? Editor);

/// <summary>
/// The identity pre-flight (§5.3, v3.1 B4): <see cref="Batches"/> without the operations of refused items, which
/// end <c>ChangedDuringApply</c> with detail <see cref="DataSyncIdentityStore.RefusedDetail"/>.
/// </summary>
public sealed record DataSyncIdentityCheck(IReadOnlyList<ApplyBatch> Batches, IReadOnlySet<string> RefusedItemIds);

/// <summary>
/// A key operation that would break the key invariant (§5.3). After the pre-flight passed, this is a store bug: the
/// apply rolls back.
/// </summary>
public sealed class DataSyncIdentityRefusedException(string kind, string key, string reason)
    : InvalidOperationException($"Data sync refused key {key} of {kind}: {reason}.")
{
    public string Kind { get; } = kind;
    public string Key { get; } = key;
    public string Reason { get; } = reason;
}

/// <summary>One entity row as the key index knows it, with every key it is known by.</summary>
public sealed record DataSyncIndexedEntity(int Id, string LocalKey, EntityKeys Keys, bool Live,
    DataSyncEntitySyncState State, DataSyncTombstoneKind? TombstoneKind)
{
    public string Primary => Keys.Primary!.Value.Value;
}

/// <summary>
/// How a record's keys bind here (§5.2). Checked in order: an exclusion; one or more live rows (two or more are an
/// identity conflict, row I; one that is LocalOnly/Detached means "not synced here", row X); tombstones (rows T);
/// nothing (natural matching, rows N).
/// </summary>
public sealed record DataSyncBinding(bool Excluded, IReadOnlyList<DataSyncIndexedEntity> Live,
    IReadOnlyList<DataSyncIndexedEntity> Tombstones)
{
    public bool IsIdentityConflict => !Excluded && Live.Count > 1;
    public DataSyncIndexedEntity? Bound => !Excluded && Live.Count == 1 ? Live[0] : null;
    public bool IsNotSyncedHere => Bound is {State: not DataSyncEntitySyncState.Synced};
    public DataSyncIndexedEntity? Tombstone => !Excluded && Live.Count == 0 && Tombstones.Count > 0 ? Tombstones[0] : null;
    public bool IsUnknown => !Excluded && Live.Count == 0 && Tombstones.Count == 0;
}

/// <summary>
/// The binding indexes of one kind (§5.2): live index (primaries and aliases of live rows in any state), tombstone
/// index (primaries and aliases of tombstoned rows, served or not) and, for one link, the exclusion index (every key
/// in <c>ExclusionKeysJson</c> of its Excluded bases). A snapshot: it does not follow later writes.
/// </summary>
public sealed class DataSyncKeyIndex
{
    private readonly Dictionary<string, DataSyncIndexedEntity> _ownerByKey;
    private readonly HashSet<string> _excluded;

    internal DataSyncKeyIndex(string kind, IReadOnlyList<DataSyncIndexedEntity> entities, IEnumerable<string> excluded)
    {
        Kind = kind;
        Entities = entities;
        _ownerByKey = new Dictionary<string, DataSyncIndexedEntity>(StringComparer.Ordinal);
        foreach (var entity in entities)
        foreach (var key in entity.Keys.All)
            _ownerByKey[key.Value] = entity;
        _excluded = new HashSet<string>(excluded, StringComparer.Ordinal);
    }

    public string Kind { get; }

    /// <summary>Every row of the kind, live and tombstoned, by Id.</summary>
    public IReadOnlyList<DataSyncIndexedEntity> Entities { get; }

    /// <summary>v3.1 §5.3: every key of a tombstone (its primary and the aliases pointing at it).</summary>
    public IReadOnlySet<SyncKey> TombstonedKeys =>
        Entities.Where(e => !e.Live).SelectMany(e => e.Keys.All).ToHashSet();

    public DataSyncIndexedEntity? OwnerOf(string key) => _ownerByKey.GetValueOrDefault(key);

    public bool IsExcluded(string key) => _excluded.Contains(key);

    public DataSyncBinding Bind(IEnumerable<string> recordKeys)
    {
        var keys = recordKeys.ToList();
        if (keys.Any(_excluded.Contains)) return new DataSyncBinding(true, [], []);
        var owners = keys.Select(OwnerOf).Where(o => o is not null).Select(o => o!).DistinctBy(o => o.Id).ToList();
        return new DataSyncBinding(false,
            owners.Where(o => o.Live).OrderBy(o => o.Id).ToList(),
            owners.Where(o => !o.Live).OrderBy(o => o.Id).ToList());
    }
}
