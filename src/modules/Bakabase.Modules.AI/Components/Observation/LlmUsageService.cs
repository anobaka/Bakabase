using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.AI.Components.Observation;

public class LlmUsageService<TDbContext>(
    ResourceService<TDbContext, LlmUsageLogDbModel, long> orm
) : ILlmUsageService where TDbContext : DbContext
{
    public async Task TrackAsync(LlmUsageLogDbModel log, CancellationToken ct = default)
    {
        await orm.Add(log);
    }

    public async Task<IReadOnlyList<LlmUsageLogDbModel>> SearchAsync(LlmUsageSearchCriteria criteria,
        CancellationToken ct = default)
    {
        var query = orm.DbContext.Set<LlmUsageLogDbModel>().AsQueryable();

        if (criteria.ProviderConfigId.HasValue)
            query = query.Where(l => l.ProviderConfigId == criteria.ProviderConfigId.Value);
        if (!string.IsNullOrEmpty(criteria.ModelId))
            query = query.Where(l => l.ModelId == criteria.ModelId);
        if (!string.IsNullOrEmpty(criteria.Feature))
            query = query.Where(l => l.Feature == criteria.Feature);
        if (criteria.StartTime.HasValue)
            query = query.Where(l => l.CreatedAt >= criteria.StartTime.Value);
        if (criteria.EndTime.HasValue)
            query = query.Where(l => l.CreatedAt <= criteria.EndTime.Value);

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(criteria.PageIndex * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(ct);
    }

    public async Task<LlmUsageSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var logs = orm.DbContext.Set<LlmUsageLogDbModel>();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var totalTokens = await logs.SumAsync(l => (long)l.TotalTokens, ct);
        var todayTokens = await logs.Where(l => l.CreatedAt >= today).SumAsync(l => (long)l.TotalTokens, ct);
        var monthTokens = await logs.Where(l => l.CreatedAt >= monthStart).SumAsync(l => (long)l.TotalTokens, ct);
        var totalCalls = await logs.CountAsync(ct);
        var cacheHits = await logs.CountAsync(l => l.CacheHit, ct);

        var byProvider = await logs
            .GroupBy(l => new { l.ProviderConfigId, l.ModelId })
            .Select(g => new LlmUsageByProvider
            {
                ProviderConfigId = g.Key.ProviderConfigId,
                ModelId = g.Key.ModelId,
                TotalTokens = g.Sum(l => (long)l.TotalTokens),
                CallCount = g.Count()
            })
            .ToListAsync(ct);

        var byFeature = await logs
            .Where(l => l.Feature != null)
            .GroupBy(l => l.Feature!)
            .Select(g => new LlmUsageByFeature
            {
                Feature = g.Key,
                TotalTokens = g.Sum(l => (long)l.TotalTokens),
                CallCount = g.Count()
            })
            .ToListAsync(ct);

        return new LlmUsageSummary
        {
            TotalTokens = totalTokens,
            TodayTokens = todayTokens,
            MonthTokens = monthTokens,
            TotalCalls = totalCalls,
            CacheHits = cacheHits,
            CacheHitRate = totalCalls > 0 ? (double)cacheHits / totalCalls : 0,
            ByProvider = byProvider,
            ByFeature = byFeature
        };
    }

    public async Task<long> GetTodayTokenUsageAsync(CancellationToken ct = default)
    {
        var today = DateTime.Today;
        return await orm.DbContext.Set<LlmUsageLogDbModel>()
            .Where(l => l.CreatedAt >= today && !l.CacheHit)
            .SumAsync(l => (long)l.TotalTokens, ct);
    }

    public async Task<long> GetMonthTokenUsageAsync(CancellationToken ct = default)
    {
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return await orm.DbContext.Set<LlmUsageLogDbModel>()
            .Where(l => l.CreatedAt >= monthStart && !l.CacheHit)
            .SumAsync(l => (long)l.TotalTokens, ct);
    }
}
