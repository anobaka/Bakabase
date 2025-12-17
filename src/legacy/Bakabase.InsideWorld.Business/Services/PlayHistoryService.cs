using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services;

public class PlayHistoryService(FullMemoryCacheResourceService<BakabaseDbContext, PlayHistoryDbModel, int> orm)
    : IPlayHistoryService
{
    public async Task Add(PlayHistoryDbModel model)
    {
        await orm.Add(model);
    }

    public async Task<List<PlayHistoryDbModel>> GetAll(Expression<Func<PlayHistoryDbModel, bool>>? selector = null)
    {
        return await orm.GetAll(selector);
    }

    public async Task<SearchResponse<PlayHistoryDbModel>> Search(Func<PlayHistoryDbModel, bool>? selector = null,
        int pageIndex = 0, int pageSize = 100)
    {
        return await orm.Search(selector, pageIndex, pageSize, [(x => x.Id, false, null)]);
    }
}