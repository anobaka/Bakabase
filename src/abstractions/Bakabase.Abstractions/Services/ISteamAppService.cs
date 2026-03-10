using Bakabase.Abstractions.Models.Db;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface ISteamAppService
{
    Task<List<SteamAppDbModel>> GetAll();
    Task<SearchResponse<SteamAppDbModel>> Search(string? keyword, int pageIndex, int pageSize);
    Task<SteamAppDbModel?> GetByAppId(int appId);
    Task<List<SteamAppDbModel>> GetByAppIds(IEnumerable<int> appIds);
    Task AddOrUpdate(SteamAppDbModel app);
    Task AddOrUpdateRange(IEnumerable<SteamAppDbModel> apps);
    Task DeleteByAppId(int appId);
    Task SyncFromApi(Func<int, int, Task>? onProgress = null, CancellationToken ct = default);
    Task SetHidden(int appId, bool isHidden);
}
