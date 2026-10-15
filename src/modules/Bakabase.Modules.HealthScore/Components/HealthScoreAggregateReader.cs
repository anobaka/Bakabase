using Bakabase.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bootstrap.Components.Orm;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Computes the aggregated final health score for a resource on demand from
/// (a) the in-memory rows in <see cref="ResourceHealthScoreOrm{TDbContext}"/>
/// and (b) the profile cache <see cref="HealthScoreProfileCache"/>.
///
/// Replaces the old <c>HealthScoreAggregateCache</c>: that cache was a
/// denormalization of these two stores and caused the well-known "no scores
/// after restart until I re-run scoring" bug because nothing populated it at
/// boot. With the orm preloaded at startup, the aggregate is cheap enough to
/// recompute per call.
///
/// Aggregation rule: among rows whose profile is enabled, take the highest
/// priority; among ties at that priority, take the lowest score.
/// </summary>
internal sealed class HealthScoreAggregateReader<TDbContext> : IResourceHealthScoreReader
    where TDbContext : DbContext, IHealthScoreDbContext
{
    private readonly ResourceHealthScoreOrm<TDbContext> _orm;
    private readonly HealthScoreProfileCache _profileCache;
    private readonly IServiceScopeFactory _scopeFactory;

    public HealthScoreAggregateReader(
        ResourceHealthScoreOrm<TDbContext> orm,
        HealthScoreProfileCache profileCache,
        IServiceScopeFactory scopeFactory)
    {
        _orm = orm;
        _profileCache = profileCache;
        _scopeFactory = scopeFactory;
    }

    public decimal? GetAggregatedScore(int resourceId)
    {
        if (PersistedReadScope.IsActive)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
            var scores = from row in db.ResourceHealthScores.AsNoTracking()
                join profile in db.HealthScoreProfiles.AsNoTracking() on row.ProfileId equals profile.Id
                where row.ResourceId == resourceId && profile.Enabled
                select new { profile.Priority, row.Score };
            // SQLite stores decimal as text; sort only this resource's bounded profile rows.
            return scores.ToList().OrderByDescending(row => row.Priority).ThenBy(row => row.Score)
                .Select(row => (decimal?)row.Score).FirstOrDefault();
        }
        var rows = _orm.GetByResource(resourceId);
        if (rows.Count == 0) return null;

        decimal? best = null;
        var bestPriority = int.MinValue;

        foreach (var row in rows)
        {
            var profile = _profileCache.Find(row.ProfileId);
            if (profile is null || !profile.Enabled) continue;

            if (best is null || profile.Priority > bestPriority)
            {
                bestPriority = profile.Priority;
                best = row.Score;
            }
            else if (profile.Priority == bestPriority && row.Score < best)
            {
                best = row.Score;
            }
        }

        return best;
    }
}
