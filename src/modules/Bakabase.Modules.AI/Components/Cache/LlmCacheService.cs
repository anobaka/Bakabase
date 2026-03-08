using Bakabase.Modules.AI.Models.Db;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.AI.Components.Cache;

public class LlmCacheService<TDbContext>(
    ResourceService<TDbContext, LlmCallCacheEntryDbModel, long> orm
) : ILlmCacheService where TDbContext : DbContext
{
    public async Task<LlmCallCacheEntryDbModel?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        var entry = await orm.DbContext.Set<LlmCallCacheEntryDbModel>()
            .FirstOrDefaultAsync(e => e.CacheKey == cacheKey, ct);

        if (entry == null) return null;

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.Now)
        {
            orm.DbContext.Set<LlmCallCacheEntryDbModel>().Remove(entry);
            await orm.DbContext.SaveChangesAsync(ct);
            return null;
        }

        entry.HitCount++;
        await orm.DbContext.SaveChangesAsync(ct);
        return entry;
    }

    public async Task SetAsync(LlmCallCacheEntryDbModel entry, CancellationToken ct = default)
    {
        var existing = await orm.DbContext.Set<LlmCallCacheEntryDbModel>()
            .FirstOrDefaultAsync(e => e.CacheKey == entry.CacheKey, ct);

        if (existing != null)
        {
            existing.ResponseJson = entry.ResponseJson;
            existing.CreatedAt = DateTime.Now;
            existing.ExpiresAt = entry.ExpiresAt;
            existing.HitCount = 0;
        }
        else
        {
            await orm.DbContext.Set<LlmCallCacheEntryDbModel>().AddAsync(entry, ct);
        }

        await orm.DbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LlmCallCacheEntryDbModel>> GetAllAsync(CancellationToken ct = default)
    {
        return await orm.DbContext.Set<LlmCallCacheEntryDbModel>()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await orm.RemoveByKey(id);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        var all = await orm.DbContext.Set<LlmCallCacheEntryDbModel>().ToListAsync(ct);
        orm.DbContext.Set<LlmCallCacheEntryDbModel>().RemoveRange(all);
        await orm.DbContext.SaveChangesAsync(ct);
    }

    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var expired = await orm.DbContext.Set<LlmCallCacheEntryDbModel>()
            .Where(e => e.ExpiresAt != null && e.ExpiresAt < DateTime.Now)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            orm.DbContext.Set<LlmCallCacheEntryDbModel>().RemoveRange(expired);
            await orm.DbContext.SaveChangesAsync(ct);
        }
    }
}
