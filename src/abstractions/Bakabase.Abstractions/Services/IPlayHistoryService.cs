using Bakabase.Abstractions.Models.Db;
using System.Linq.Expressions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface IPlayHistoryService
{
    Task Add(PlayHistoryDbModel model);
    Task<List<PlayHistoryDbModel>> GetAll(Expression<Func<PlayHistoryDbModel, bool>>? selector = null);

    Task<SearchResponse<PlayHistoryDbModel>> Search(Func<PlayHistoryDbModel, bool>? selector = null, int pageIndex = 0,
        int pageSize = 100);
}