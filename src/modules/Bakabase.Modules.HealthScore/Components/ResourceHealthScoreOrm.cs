using System.Collections.Concurrent;
using Bakabase.Modules.HealthScore.Models.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Full-memory cache for the <c>ResourceHealthScore</c> table. The table has a
/// composite primary key <c>(ResourceId, ProfileId)</c>, so the generic
/// <c>FullMemoryCacheResourceService&lt;TDbContext, TResource, TKey&gt;</c>
/// (single-PK only) does not fit; this is a purpose-built equivalent.
///
/// Loaded once at startup via <see cref="HealthScoreCacheWarmer{TDbContext}"/>.
/// Reads are pure dictionary lookups; writes go through the DB and then update
/// the in-memory state. Singleton.
/// </summary>
public sealed class ResourceHealthScoreOrm<TDbContext>
    where TDbContext : DbContext, IHealthScoreDbContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>resourceId → (profileId → row). Outer dict drives the hot-path
    /// "fetch all rows for this resource" used by the search index.</summary>
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, ResourceHealthScoreDbModel>> _byResource = new();

    public ResourceHealthScoreOrm(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // ============ Bootstrap ============

    public async Task Initialize(CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var rows = await ctx.ResourceHealthScores.AsNoTracking().ToListAsync(ct);

        _byResource.Clear();
        foreach (var row in rows)
        {
            var inner = _byResource.GetOrAdd(row.ResourceId, _ => new ConcurrentDictionary<int, ResourceHealthScoreDbModel>());
            inner[row.ProfileId] = row;
        }
    }

    // ============ Reads (pure memory) ============

    public ResourceHealthScoreDbModel? Get(int resourceId, int profileId) =>
        _byResource.TryGetValue(resourceId, out var inner) && inner.TryGetValue(profileId, out var row)
            ? Clone(row)
            : null;

    /// <summary>Snapshot copies of every row for this resource. Empty when none.</summary>
    public IReadOnlyList<ResourceHealthScoreDbModel> GetByResource(int resourceId)
    {
        if (!_byResource.TryGetValue(resourceId, out var inner) || inner.IsEmpty)
            return Array.Empty<ResourceHealthScoreDbModel>();

        var list = new List<ResourceHealthScoreDbModel>(inner.Count);
        foreach (var row in inner.Values) list.Add(Clone(row));
        return list;
    }

    /// <summary>
    /// Resource ids that currently have a row for the given profile. When
    /// <paramref name="matchingHash"/> is supplied, also requires
    /// <c>ProfileHash</c> to equal it (used to skip already-fresh rows).
    /// </summary>
    public HashSet<int> GetResourceIdsForProfile(int profileId, string? matchingHash = null)
    {
        var result = new HashSet<int>();
        foreach (var (rid, inner) in _byResource)
        {
            if (inner.TryGetValue(profileId, out var row) &&
                (matchingHash is null || row.ProfileHash == matchingHash))
            {
                result.Add(rid);
            }
        }
        return result;
    }

    public HashSet<int> GetAllScoredResourceIds() => [.. _byResource.Keys];

    // ============ Writes (DB + memory) ============

    /// <summary>
    /// Insert-or-update a batch of rows in a single SaveChanges. Uses the
    /// in-memory cache to decide insert vs. update — no extra SELECT
    /// round-trips. Cache is updated only after the DB write succeeds.
    /// </summary>
    public async Task UpsertBatch(IEnumerable<ResourceHealthScoreDbModel> rows, CancellationToken ct = default)
    {
        var rowList = rows as List<ResourceHealthScoreDbModel> ?? rows.ToList();
        if (rowList.Count == 0) return;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();

        foreach (var row in rowList)
        {
            // EF AddRange/UpdateRange require disambiguation here because we
            // have a composite PK and can't rely on the default PK convention.
            if (Get(row.ResourceId, row.ProfileId) is null)
            {
                ctx.ResourceHealthScores.Add(row);
            }
            else
            {
                ctx.ResourceHealthScores.Update(row);
            }
        }
        await ctx.SaveChangesAsync(ct);

        foreach (var row in rowList)
        {
            var inner = _byResource.GetOrAdd(row.ResourceId, _ => new ConcurrentDictionary<int, ResourceHealthScoreDbModel>());
            inner[row.ProfileId] = Clone(row);
        }
    }

    /// <summary>
    /// Delete every row for a profile. Returns the resource ids that lost a row
    /// (so the caller can invalidate aggregates / search-index entries).
    /// </summary>
    public async Task<HashSet<int>> RemoveByProfile(int profileId, CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await ctx.ResourceHealthScores
            .Where(r => r.ProfileId == profileId)
            .ExecuteDeleteAsync(ct);

        var affected = new HashSet<int>();
        foreach (var (rid, inner) in _byResource)
        {
            if (inner.TryRemove(profileId, out _)) affected.Add(rid);
        }
        return affected;
    }

    /// <summary>
    /// Delete rows of <paramref name="profileId"/> whose ResourceId is NOT in
    /// <paramref name="keepResources"/> (used by the membership-eviction step
    /// of a scoring run). Returns the affected resource ids.
    /// </summary>
    public async Task<HashSet<int>> RemoveByProfileWhereResourceNotIn(
        int profileId, HashSet<int> keepResources, CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await ctx.ResourceHealthScores
            .Where(r => r.ProfileId == profileId && !keepResources.Contains(r.ResourceId))
            .ExecuteDeleteAsync(ct);

        var affected = new HashSet<int>();
        foreach (var (rid, inner) in _byResource)
        {
            if (!keepResources.Contains(rid) && inner.TryRemove(profileId, out _)) affected.Add(rid);
        }
        return affected;
    }

    /// <summary>Reset <c>ProfileHash</c> to empty for every row of a profile.</summary>
    public async Task ClearProfileHash(int profileId, CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await ctx.ResourceHealthScores
            .Where(r => r.ProfileId == profileId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProfileHash, _ => string.Empty), ct);

        foreach (var (_, inner) in _byResource)
        {
            if (inner.TryGetValue(profileId, out var row))
            {
                row.ProfileHash = string.Empty;
            }
        }
    }

    /// <summary>Reset <c>ProfileHash</c> to empty across all profiles.</summary>
    public async Task ClearAllHashes(CancellationToken ct = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await ctx.ResourceHealthScores
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProfileHash, _ => string.Empty), ct);

        foreach (var (_, inner) in _byResource)
        {
            foreach (var row in inner.Values) row.ProfileHash = string.Empty;
        }
    }

    private static ResourceHealthScoreDbModel Clone(ResourceHealthScoreDbModel r) => new()
    {
        ResourceId = r.ResourceId,
        ProfileId = r.ProfileId,
        Score = r.Score,
        ProfileHash = r.ProfileHash,
        MatchedRulesJson = r.MatchedRulesJson,
        EvaluatedAt = r.EvaluatedAt,
    };
}
