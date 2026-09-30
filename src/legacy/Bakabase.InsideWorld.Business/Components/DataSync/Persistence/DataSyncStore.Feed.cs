using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

// What the feed source reads beyond IDataSyncStore (§7.5): per-kind sequence heads, the keys records are served
// under, and the stored vectors a head's SeenCounter is read from.
public sealed partial class DataSyncStore
{
    /// <summary>
    /// The highest Seq of every kind that has rows, served or not (§7.5.2 <c>MaxSeq</c>): tombstones, unserved
    /// tombstones and entities kept on this device only included, so a reader's cursor moves past changes it is
    /// never served.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, long>> GetKindMaxSeqsAsync(CancellationToken ct)
    {
        await FlushAsync(ct);
        var rows = await _db.DataSyncEntities.AsNoTracking()
            .GroupBy(e => e.Kind)
            .Select(g => new {Kind = g.Key, MaxSeq = g.Max(e => e.Seq)})
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Kind, r => r.MaxSeq, StringComparer.Ordinal);
    }

    /// <summary>The aliases of every entity of <paramref name="kind"/>, by primary key, ordinally sorted (§5.3).</summary>
    internal async Task<ILookup<string, string>> GetAliasesByPrimaryAsync(string kind, CancellationToken ct)
    {
        await FlushAsync(ct);
        var aliases = await _db.DataSyncKeyAliases.AsNoTracking()
            .Where(a => a.Kind == kind)
            .Select(a => new {a.SyncKey, a.AliasKey})
            .ToListAsync(ct);
        return aliases.OrderBy(a => a.AliasKey, StringComparer.Ordinal)
            .ToLookup(a => a.SyncKey, a => a.AliasKey, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every stored vector that may name <paramref name="actorId"/>, as stored JSON: entities and tombstones, the
    /// bases' last agreements and the pending records (whole records; the caller reads their <c>vv</c>). Read per head
    /// for its SeenCounter (§7.5.1 step 4); rows whose JSON does not contain the actor are left out by the query.
    /// </summary>
    internal async Task<(IReadOnlyList<string> Vectors, IReadOnlyList<string> PendingRecords)> ReadVectorsNamingAsync(
        string actorId, CancellationToken ct)
    {
        await FlushAsync(ct);
        var vectors = await _db.DataSyncEntities.AsNoTracking().Where(e => e.VvJson.Contains(actorId))
            .Select(e => e.VvJson).ToListAsync(ct);
        vectors.AddRange(await _db.DataSyncPeerBases.AsNoTracking()
            .Where(b => b.VvJson != null && b.VvJson.Contains(actorId))
            .Select(b => b.VvJson!)
            .ToListAsync(ct));
        var pending = await _db.DataSyncPeerBases.AsNoTracking()
            .Where(b => b.PendingRecordJson != null && b.PendingRecordJson.Contains(actorId))
            .Select(b => b.PendingRecordJson!)
            .ToListAsync(ct);
        return (vectors, pending);
    }
}
