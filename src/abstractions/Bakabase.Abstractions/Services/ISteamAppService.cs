using Bakabase.Abstractions.Models.Db;

namespace Bakabase.Abstractions.Services;

public interface ISteamAppService
{
    Task<List<SteamAppDbModel>> GetAll();
    Task<SteamAppDbModel?> GetByAppId(int appId);
    Task<List<SteamAppDbModel>> GetByAppIds(IEnumerable<int> appIds);
    Task AddOrUpdate(SteamAppDbModel app);
    Task AddOrUpdateRange(IEnumerable<SteamAppDbModel> apps);
    Task DeleteByAppId(int appId);
}
