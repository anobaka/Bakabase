using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services;

public interface ICustomPropertyService
{
    Task<List<Abstractions.Models.Domain.CustomProperty>> GetAll(
        Expression<Func<CustomProperty, bool>>? selector = null,
        CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
        bool returnCopy = true);

    Task<List<CustomProperty>> GetAll(Expression<Func<CustomProperty, bool>> selector = null, bool returnCopy = true);

    Task<Abstractions.Models.Domain.CustomProperty> GetByKey(int id,
        CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
        bool returnCopy = true);

    Task<CustomProperty> GetByKey(Int32 key, bool returnCopy = true);

    Task<List<Abstractions.Models.Domain.CustomProperty>> GetByKeys(IEnumerable<int> ids,
        CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
        bool returnCopy = true);

    Task<CustomProperty[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    Task<Dictionary<int, List<Abstractions.Models.Domain.CustomProperty>>> GetByCategoryIds(int[] ids);
    Task<Abstractions.Models.Domain.CustomProperty> Add(CustomPropertyAddOrPutDto model);
    Task<SingletonResponse<CustomProperty>> Add(CustomProperty resource);
    Task<Abstractions.Models.Domain.CustomProperty> Put(int id, CustomPropertyAddOrPutDto model);
    Task<BaseResponse> RemoveByKey(int id);

    Task<CustomPropertyTypeConversionLossViewModel> CalculateTypeConversionLoss(int id,
        CustomPropertyType type);
    Task<CustomProperty> GetFirst(Expression<Func<CustomProperty, bool>> selector,
        Expression<Func<CustomProperty, object>> orderBy = null, bool asc = false, bool returnCopy = true);

    Task<int> Count(Func<CustomProperty, bool> selector = null);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="pageIndex"></param>
    /// <param name="pageSize"></param>
    /// <param name="orders">Key Selector - Asc</param>
    /// <param name="returnCopy"></param>
    /// <returns></returns>
    Task<SearchResponse<CustomProperty>> Search(Func<CustomProperty, bool> selector,
        int pageIndex, int pageSize, (Func<CustomProperty, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
        bool returnCopy = true);

    Task<SearchResponse<CustomProperty>> Search(Func<CustomProperty, bool> selector,
        int pageIndex, int pageSize, Func<CustomProperty, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);

    Task<BaseResponse> Remove(CustomProperty resource);
    Task<BaseResponse> RemoveRange(IEnumerable<CustomProperty> resources);
    Task<BaseResponse> RemoveAll(Expression<Func<CustomProperty, bool>> selector);
    Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    Task<ListResponse<CustomProperty>> AddRange(List<CustomProperty> resources);
    Task<SingletonResponse<CustomProperty>> UpdateByKey(Int32 key, Action<CustomProperty> modify);
    Task<BaseResponse> Update(CustomProperty resource);
    Task<BaseResponse> UpdateRange(IReadOnlyCollection<CustomProperty> resources);

    Task<ListResponse<CustomProperty>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
        Action<CustomProperty> modify);

    Task<SingletonResponse<CustomProperty>> UpdateFirst(Expression<Func<CustomProperty, bool>> selector,
        Action<CustomProperty> modify);

    Task<ListResponse<CustomProperty>> UpdateAll(Expression<Func<CustomProperty, bool>> selector,
        Action<CustomProperty> modify);
}