using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Alias;

public interface IAliasService
{
    // Task AddRange(HashSet<string> texts);
    Task SaveByResources(List<Models.Domain.Resource> resources);
    // Task<ListResponse<Abstractions.Models.Db.Alias>> AddRange(List<Abstractions.Models.Db.Alias> resources);
    // Task<Abstractions.Models.Db.Alias> GetByKey(Int32 key, bool returnCopy = true);
    // Task<Abstractions.Models.Db.Alias[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    // Task<Abstractions.Models.Db.Alias> GetFirst(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Expression<Func<Abstractions.Models.Db.Alias, object>> orderBy = null, bool asc = false, bool returnCopy = true);
    //
    // Task<List<Abstractions.Models.Db.Alias>> GetAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector = null, bool returnCopy = true);
    // Task<int> Count(Func<Abstractions.Models.Db.Alias, bool> selector = null);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="pageIndex"></param>
    // /// <param name="pageSize"></param>
    // /// <param name="orders">Key Selector - Asc</param>
    // /// <param name="returnCopy"></param>
    // /// <returns></returns>
    // Task<SearchResponse<Abstractions.Models.Db.Alias>> Search(Func<Abstractions.Models.Db.Alias, bool> selector,
    //     int pageIndex, int pageSize, (Func<Abstractions.Models.Db.Alias, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<Abstractions.Models.Db.Alias>> Search(Func<Abstractions.Models.Db.Alias, bool> selector,
    //     int pageIndex, int pageSize, Func<Abstractions.Models.Db.Alias, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(Abstractions.Models.Db.Alias resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Abstractions.Models.Db.Alias> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector);
    // Task<BaseResponse> RemoveByKey(Int32 key);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> Add(Abstractions.Models.Db.Alias resource);
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> UpdateByKey(Int32 key, Action<Abstractions.Models.Db.Alias> modify);
    // Task<BaseResponse> Update(Abstractions.Models.Db.Alias resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<Abstractions.Models.Db.Alias> resources);
    //
    // Task<ListResponse<Abstractions.Models.Db.Alias>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Abstractions.Models.Db.Alias> modify);
    //
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> UpdateFirst(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Action<Abstractions.Models.Db.Alias> modify);
    //
    // Task<ListResponse<Abstractions.Models.Db.Alias>> UpdateAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Action<Abstractions.Models.Db.Alias> modify);
}