using System.Linq.Expressions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.CustomProperty.Abstractions.Services;

public interface ICategoryCustomPropertyMappingService
{
    Task BindCustomPropertiesToCategory(int categoryId, int[]? customPropertyIds);
    // Task<CategoryCustomPropertyMapping> GetByKey(int key, bool returnCopy = true);
    // Task<CategoryCustomPropertyMapping[]> GetByKeys(IEnumerable<int> keys, bool returnCopy = true);
    //
    // Task<CategoryCustomPropertyMapping> GetFirst(Expression<Func<CategoryCustomPropertyMapping, bool>> selector,
    //     Expression<Func<CategoryCustomPropertyMapping, object>> orderBy = null, bool asc = false, bool returnCopy = true);
    //
    Task<List<CategoryCustomPropertyMapping>> GetAll(Expression<Func<CategoryCustomPropertyMapping, bool>> selector = null, bool returnCopy = true);
    // Task<int> Count(Func<CategoryCustomPropertyMapping, bool> selector = null);
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
    // Task<SearchResponse<CategoryCustomPropertyMapping>> Search(Func<CategoryCustomPropertyMapping, bool> selector,
    //     int pageIndex, int pageSize, (Func<CategoryCustomPropertyMapping, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<CategoryCustomPropertyMapping>> Search(Func<CategoryCustomPropertyMapping, bool> selector,
    //     int pageIndex, int pageSize, Func<CategoryCustomPropertyMapping, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(CategoryCustomPropertyMapping resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<CategoryCustomPropertyMapping> resources);
    Task<BaseResponse> RemoveAll(Expression<Func<CategoryCustomPropertyMapping, bool>> selector);
    // Task<BaseResponse> RemoveByKey(int key);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<int> keys);
    // Task<SingletonResponse<CategoryCustomPropertyMapping>> Add(CategoryCustomPropertyMapping resource);
    // Task<ListResponse<CategoryCustomPropertyMapping>> AddRange(List<CategoryCustomPropertyMapping> resources);
    // Task<SingletonResponse<CategoryCustomPropertyMapping>> UpdateByKey(int key, Action<CategoryCustomPropertyMapping> modify);
    // Task<BaseResponse> Update(CategoryCustomPropertyMapping resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<CategoryCustomPropertyMapping> resources);
    //
    // Task<ListResponse<CategoryCustomPropertyMapping>> UpdateByKeys(IReadOnlyCollection<int> keys,
    //     Action<CategoryCustomPropertyMapping> modify);
    //
    // Task<SingletonResponse<CategoryCustomPropertyMapping>> UpdateFirst(Expression<Func<CategoryCustomPropertyMapping, bool>> selector,
    //     Action<CategoryCustomPropertyMapping> modify);
    //
    // Task<ListResponse<CategoryCustomPropertyMapping>> UpdateAll(Expression<Func<CategoryCustomPropertyMapping, bool>> selector,
    //     Action<CategoryCustomPropertyMapping> modify);
}