using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface ITagResourceMappingService
{
    // Task<TagResourceMapping> GetByKey(Int32 key, bool returnCopy = true);
    // Task<TagResourceMapping[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    // Task<TagResourceMapping> GetFirst(Expression<Func<TagResourceMapping, bool>> selector,
    //     Expression<Func<TagResourceMapping, object>> orderBy = null, bool asc = false, bool returnCopy = true);

    Task<List<TagResourceMapping>> GetAll(Expression<Func<TagResourceMapping, bool>> selector = null, bool returnCopy = true);
    // Task<int> Count(Func<TagResourceMapping, bool> selector = null);
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
    // Task<SearchResponse<TagResourceMapping>> Search(Func<TagResourceMapping, bool> selector,
    //     int pageIndex, int pageSize, (Func<TagResourceMapping, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<TagResourceMapping>> Search(Func<TagResourceMapping, bool> selector,
    //     int pageIndex, int pageSize, Func<TagResourceMapping, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(TagResourceMapping resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<TagResourceMapping> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<TagResourceMapping, bool>> selector);
    // Task<BaseResponse> RemoveByKey(Int32 key);
    Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<SingletonResponse<TagResourceMapping>> Add(TagResourceMapping resource);
    Task<ListResponse<TagResourceMapping>> AddRange(List<TagResourceMapping> resources);
    // Task<SingletonResponse<TagResourceMapping>> UpdateByKey(Int32 key, Action<TagResourceMapping> modify);
    // Task<BaseResponse> Update(TagResourceMapping resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<TagResourceMapping> resources);
    //
    // Task<ListResponse<TagResourceMapping>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<TagResourceMapping> modify);
    //
    // Task<SingletonResponse<TagResourceMapping>> UpdateFirst(Expression<Func<TagResourceMapping, bool>> selector,
    //     Action<TagResourceMapping> modify);
    //
    // Task<ListResponse<TagResourceMapping>> UpdateAll(Expression<Func<TagResourceMapping, bool>> selector,
    //     Action<TagResourceMapping> modify);
}