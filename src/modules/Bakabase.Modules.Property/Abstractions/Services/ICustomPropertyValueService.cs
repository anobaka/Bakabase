using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Property.Abstractions.Services;

public interface ICustomPropertyValueService
{
    Task<List<CustomPropertyValue>> GetAll(
        Expression<Func<CustomPropertyValueDbModel, bool>>? exp,
        CustomPropertyValueAdditionalItem additionalItems, bool returnCopy);

    Task SaveByResources(List<Resource> data);

    Task<(CustomPropertyValue Value, bool PropertyChanged)?> CreateTransient(object? bizValue, StandardValueType bizValueType,
        Bakabase.Abstractions.Models.Domain.CustomProperty customProperty, int resourceId, int scope);

    Task<List<CustomPropertyValueDbModel>> GetAllDbModels(
        Expression<Func<CustomPropertyValueDbModel, bool>> selector = null,
        bool returnCopy = true);

    /// <summary>
    /// Get count of values grouped by property ID
    /// </summary>
    Task<Dictionary<int, int>> GetCountByPropertyIds(IEnumerable<int> propertyIds);
    //
    Task<BaseResponse> AddRange(IEnumerable<CustomPropertyValue> values);

    // Task<ListResponse<CustomPropertyValue>> AddRange(List<CustomPropertyValue> resources);
    Task<BaseResponse> UpdateRange(IEnumerable<CustomPropertyValue> values);

    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<CustomPropertyValue> resources);
    // Task<CustomPropertyValue> GetByKey(Int32 key, bool returnCopy = true);
    // Task<CustomPropertyValue[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    
    // Task<CustomPropertyValue?> GetFirst(Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, bool>>? selector,
    //     Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, object>>? orderBy = null, bool asc = false, bool returnCopy = true);
    //
    // Task<int> Count(Func<CustomPropertyValue, bool> selector = null);
    Task<bool> Any(Func<CustomPropertyValueDbModel, bool>? selector = null);
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
    // Task<SearchResponse<CustomPropertyValue>> Search(Func<CustomPropertyValue, bool> selector,
    //     int pageIndex, int pageSize, (Func<CustomPropertyValue, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<CustomPropertyValue>> Search(Func<CustomPropertyValue, bool> selector,
    //     int pageIndex, int pageSize, Func<CustomPropertyValue, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    Task<BaseResponse> Remove(CustomPropertyValueDbModel resource);
    Task<BaseResponse> RemoveRange(IEnumerable<CustomPropertyValueDbModel> resources);

    Task<BaseResponse> RemoveAll(Expression<Func<CustomPropertyValueDbModel, bool>> selector);

    // Task<BaseResponse> RemoveByKey(Int32 key);
    Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    Task<SingletonResponse<CustomPropertyValueDbModel>> AddDbModel(CustomPropertyValueDbModel resource);
    Task<BaseResponse> AddDbModelRange(IEnumerable<CustomPropertyValueDbModel> resources);
    // Task<SingletonResponse<CustomPropertyValue>> UpdateByKey(Int32 key, Action<CustomPropertyValue> modify);
    Task<BaseResponse> UpdateDbModel(CustomPropertyValueDbModel resource);
    Task<BaseResponse> UpdateDbModelRange(IEnumerable<CustomPropertyValueDbModel> resources);
    //
    // Task<ListResponse<CustomPropertyValue>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<CustomPropertyValue> modify);
    //
    // Task<SingletonResponse<CustomPropertyValue>> UpdateFirst(Expression<Func<CustomPropertyValue, bool>> selector,
    //     Action<CustomPropertyValue> modify);
    //
    // Task<ListResponse<CustomPropertyValue>> UpdateAll(Expression<Func<CustomPropertyValue, bool>> selector,
    //     Action<CustomPropertyValue> modify);
}