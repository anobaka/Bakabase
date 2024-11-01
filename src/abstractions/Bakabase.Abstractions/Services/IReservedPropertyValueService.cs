using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bootstrap.Models.ResponseModels;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.Abstractions.Services;

public interface IReservedPropertyValueService
{
    // Task<ResourceReservedPropertyValue> GetByKey(Int32 key, bool returnCopy = true);
    // Task<ResourceReservedPropertyValue[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    Task<ReservedPropertyValue?>
        GetFirst(Expression<Func<Abstractions.Models.Db.ReservedPropertyValue, bool>> selector);

    Task<List<ReservedPropertyValue>> GetAll(
        Expression<Func<Bakabase.Abstractions.Models.Db.ReservedPropertyValue, bool>>? selector = null,
        bool asNoTracking = true);

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
    Task<SingletonResponse<ReservedPropertyValue>> Add(ReservedPropertyValue resource);

    Task<ListResponse<ReservedPropertyValue>> AddRange(List<ReservedPropertyValue> resources);

    // Task<SingletonResponse<ResourceReservedPropertyValue>> UpdateByKey(Int32 key, Action<ResourceReservedPropertyValue> modify);
    Task<BaseResponse> Update(ReservedPropertyValue resource);
    Task<BaseResponse> UpdateRange(IReadOnlyCollection<ReservedPropertyValue> resources);

    /// <summary>
    /// Based on BizValue.
    /// </summary>
    /// <param name="resources"></param>
    /// <returns></returns>
    Task<BaseResponse> PutByResources(List<Resource> resources);

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