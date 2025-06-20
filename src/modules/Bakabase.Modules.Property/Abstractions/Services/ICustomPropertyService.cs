﻿using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Models.View;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Property.Abstractions.Services;

public interface ICustomPropertyService
{
    // Task<BaseResponse> EnableAddingNewDataDynamically(int id);

    Task<List<Bakabase.Abstractions.Models.Domain.CustomProperty>> GetAll(
        Expression<Func<CustomPropertyDbModel, bool>>? selector = null,
        CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
        bool returnCopy = true);

    // Task<List<Bakabase.Abstractions.Models.Db.CustomProperty>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>> selector = null, bool returnCopy = true);
    //
    Task<CustomProperty> GetByKey(int id, CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None, bool returnCopy = true);

    //
    // Task<Bakabase.Abstractions.Models.Db.CustomProperty> GetByKey(Int32 key, bool returnCopy = true);
    //
    Task<List<CustomProperty>> GetByKeys(IEnumerable<int> ids,
        CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
        bool returnCopy = true);

    //
    // Task<Bakabase.Abstractions.Models.Db.CustomProperty[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    Task<Dictionary<int, List<CustomProperty>>> GetByCategoryIds(int[] ids);
    Task<CustomProperty> Add(CustomPropertyAddOrPutDto model);

    Task<List<CustomProperty>> AddRange(CustomPropertyAddOrPutDto[] models);

    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> Add(Bakabase.Abstractions.Models.Db.CustomProperty resource);
    Task<CustomProperty> Put(int id, CustomPropertyAddOrPutDto model);
    Task Sort(int[] ids);
    Task<BaseResponse> RemoveByKey(int id);

    Task<CustomPropertyTypeConversionPreviewViewModel> PreviewTypeConversion(int sourcePropertyId, PropertyType toType);

    Task<BaseResponse> ChangeType(int sourcePropertyId, PropertyType type);

    // Task<Bakabase.Abstractions.Models.Db.CustomProperty> GetFirst(Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>> selector,
    //     Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, object>> orderBy = null, bool asc = false, bool returnCopy = true);
    //
    // Task<int> Count(Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool> selector = null);
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
    // Task<SearchResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> Search(Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool> selector,
    //     int pageIndex, int pageSize, (Func<Bakabase.Abstractions.Models.Db.CustomProperty, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> Search(Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool> selector,
    //     int pageIndex, int pageSize, Func<Bakabase.Abstractions.Models.Db.CustomProperty, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(Bakabase.Abstractions.Models.Db.CustomProperty resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Bakabase.Abstractions.Models.Db.CustomProperty> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>> selector);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> AddRange(List<Bakabase.Abstractions.Models.Db.CustomProperty> resources);
    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> UpdateByKey(Int32 key, Action<Bakabase.Abstractions.Models.Db.CustomProperty> modify);
    Task<BaseResponse> Put(CustomProperty resource);

    Task<BaseResponse> UpdateRange(IReadOnlyCollection<CustomPropertyDbModel> resources);
    //
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Bakabase.Abstractions.Models.Db.CustomProperty> modify);
    //
    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> UpdateFirst(Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>> selector,
    //     Action<Bakabase.Abstractions.Models.Db.CustomProperty> modify);
    //
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.CustomProperty>> UpdateAll(Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>> selector,
    //     Action<Bakabase.Abstractions.Models.Db.CustomProperty> modify);
}