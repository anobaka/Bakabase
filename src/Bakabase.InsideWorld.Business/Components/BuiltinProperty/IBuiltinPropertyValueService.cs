using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Models.ResponseModels;
using BuiltinPropertyValue = Bakabase.Abstractions.Models.Domain.BuiltinPropertyValue;

namespace Bakabase.InsideWorld.Business.Components.BuiltinProperty;

public interface IBuiltinPropertyValueService
{
    // Task<ResourceReservedPropertyValue> GetByKey(Int32 key, bool returnCopy = true);
    // Task<ResourceReservedPropertyValue[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    // Task<ResourceReservedPropertyValue> GetFirst(Expression<Func<ResourceReservedPropertyValue, bool>> selector,
    //     Expression<Func<ResourceReservedPropertyValue, object>> orderBy = null, bool asc = false, bool returnCopy = true);

    Task<List<BuiltinPropertyValue>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.BuiltinPropertyValue, bool>>? selector = null, bool asNoTracking = true);
    // Task<int> Count(Func<ResourceReservedPropertyValue, bool> selector = null);
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
    // Task<SearchResponse<ResourceReservedPropertyValue>> Search(Func<ResourceReservedPropertyValue, bool> selector,
    //     int pageIndex, int pageSize, (Func<ResourceReservedPropertyValue, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<ResourceReservedPropertyValue>> Search(Func<ResourceReservedPropertyValue, bool> selector,
    //     int pageIndex, int pageSize, Func<ResourceReservedPropertyValue, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(ResourceReservedPropertyValue resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<ResourceReservedPropertyValue> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<ResourceReservedPropertyValue, bool>> selector);
    // Task<BaseResponse> RemoveByKey(Int32 key);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<SingletonResponse<ResourceReservedPropertyValue>> Add(ResourceReservedPropertyValue resource);
    Task<ListResponse<BuiltinPropertyValue>> AddRange(List<BuiltinPropertyValue> resources);
    // Task<SingletonResponse<ResourceReservedPropertyValue>> UpdateByKey(Int32 key, Action<ResourceReservedPropertyValue> modify);
    // Task<BaseResponse> Update(ResourceReservedPropertyValue resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<ResourceReservedPropertyValue> resources);
    //
    // Task<ListResponse<ResourceReservedPropertyValue>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<ResourceReservedPropertyValue> modify);
    //
    // Task<SingletonResponse<ResourceReservedPropertyValue>> UpdateFirst(Expression<Func<ResourceReservedPropertyValue, bool>> selector,
    //     Action<ResourceReservedPropertyValue> modify);
    //
    // Task<ListResponse<ResourceReservedPropertyValue>> UpdateAll(Expression<Func<ResourceReservedPropertyValue, bool>> selector,
    //     Action<ResourceReservedPropertyValue> modify);
}