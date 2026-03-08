using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class SteamAppService(FullMemoryCacheResourceService<BakabaseDbContext, SteamAppDbModel, int> orm)
    : ISteamAppService
{
    public async Task<List<SteamAppDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<SteamAppDbModel?> GetByAppId(int appId)
    {
        return await orm.GetFirstOrDefault(x => x.AppId == appId);
    }

    public async Task<List<SteamAppDbModel>> GetByAppIds(IEnumerable<int> appIds)
    {
        var ids = appIds.ToHashSet();
        return await orm.GetAll(x => ids.Contains(x.AppId));
    }

    public async Task AddOrUpdate(SteamAppDbModel app)
    {
        var existing = await orm.GetFirstOrDefault(x => x.AppId == app.AppId);
        if (existing != null)
        {
            app.Id = existing.Id;
            app.UpdatedAt = DateTime.Now;
            await orm.Update(app);
        }
        else
        {
            app.CreatedAt = DateTime.Now;
            app.UpdatedAt = DateTime.Now;
            await orm.Add(app);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<SteamAppDbModel> apps)
    {
        foreach (var app in apps)
        {
            await AddOrUpdate(app);
        }
    }

    public async Task DeleteByAppId(int appId)
    {
        var existing = await orm.GetFirstOrDefault(x => x.AppId == appId);
        if (existing != null)
        {
            await orm.RemoveByKey(existing.Id);
        }
    }
}
