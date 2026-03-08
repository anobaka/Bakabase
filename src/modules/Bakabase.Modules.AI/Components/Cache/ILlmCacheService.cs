using Bakabase.Modules.AI.Models.Db;

namespace Bakabase.Modules.AI.Components.Cache;

public interface ILlmCacheService
{
    Task<LlmCallCacheEntryDbModel?> GetAsync(string cacheKey, CancellationToken ct = default);
    Task SetAsync(LlmCallCacheEntryDbModel entry, CancellationToken ct = default);
    Task<IReadOnlyList<LlmCallCacheEntryDbModel>> GetAllAsync(CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
    Task CleanupExpiredAsync(CancellationToken ct = default);
}
