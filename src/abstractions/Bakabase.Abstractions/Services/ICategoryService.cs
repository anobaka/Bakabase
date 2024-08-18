using System.Linq.Expressions;
using Bakabase.Abstractions.Components.Component;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

public interface ICategoryService
{
    // Task<ListResponse<TComponent>> GetComponents<TComponent>(Abstractions.Models.Domain.Category category,
    //     ComponentType type)
    //     where TComponent : class, IComponent;
    //
    // Task<ListResponse<TComponent>> GetComponents<TComponent>(int id, ComponentType type)
    //     where TComponent : class, IComponent;
    //
    Task<SingletonResponse<TComponent?>> GetFirstComponent<TComponent>(int id, ComponentType type) where TComponent : class, IComponent;
    //
    // Task<BaseResponse> Play(int id, string file);

    Task<List<Category>> GetAll(Expression<Func<Models.Db.Category, bool>>? selector = null,
        CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None);

    Task<Category?> Get(int id, CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="key"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<Category> GetByKey(Int32 key, bool asNoTracking = false);
    //
    Task<SingletonResponse<Category>> Add(CategoryAddInputModel model);
    //
    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resource"></param>
    // /// <returns></returns>
    // Task<SingletonResponse<Category>> Add(Category resource);
    //
    Task<SingletonResponse<Category>> Duplicate(int id, CategoryDuplicateInputModel model);
    //
    Task<BaseResponse> ConfigureComponents(int id, CategoryComponentConfigureInputModel model);
    //
    Task<BaseResponse> Patch(int id, CategoryPatchInputModel model);

    Task<BaseResponse> Delete(int id);
    
    Task<BaseResponse> Sort(int[] ids);
    // Task<BaseResponse> SaveDataFromSetupWizard(CategorySetupWizardInputModel model);
    Task<BaseResponse> BindCustomProperty(int categoryId, int propertyId);
    Task<BaseResponse> BindCustomProperties(int id, CategoryCustomPropertyBindInputModel model);
    //
    // CategoryResourceDisplayNameViewModel.Segment[] BuildDisplayNameSegmentsForResource(Resource resource, string template, (string Left, string Right)[] wrappers);
    string BuildDisplayNameForResource(Resource resource, string template, (string Left, string Right)[] wrappers);

    Task<List<CategoryResourceDisplayNameViewModel>> PreviewResourceDisplayNameTemplate(int id, string template, int maxCount = 100);
    //
    // /// <summary>
    // /// 获取单条默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="orderBy"></param>
    // /// <param name="asc"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<Category> GetFirst(Expression<Func<Category, bool>> selector,
    //     Expression<Func<Category, object>> orderBy = null,
    //     bool asc = false, bool asNoTracking = false);
    //
    // /// <summary>
    // /// 获取全部默认资源
    // /// </summary>
    // /// <param name="selector">为空则获取全部</param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<List<Category>> GetAll(Expression<Func<Category, bool>> selector = null, bool asNoTracking = false);
    //
    // /// <summary>
    // /// 搜索默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="pageIndex">从1开始</param>
    // /// <param name="pageSize"></param>
    // /// <param name="orderBy"></param>
    // /// <param name="asc"></param>
    // /// <param name="include"></param>
    // /// <returns></returns>
    // Task<SearchResponse<Category>> Search(
    //     Expression<Func<Category, bool>> selector, int pageIndex, int pageSize,
    //     Expression<Func<Category, object>> orderBy = null, bool asc = false,
    //     Expression<Func<Category, object>> include = null);
    //
    // Task<BaseResponse> Remove(Category resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Category> resources);
    //
    // /// <summary>
    // /// 删除默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <returns></returns>
    // Task<BaseResponse> RemoveAll(Expression<Func<Category, bool>> selector);
    //
    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resources"></param>
    // /// <returns></returns>
    // Task<ListResponse<Category>> AddRange(IEnumerable<Category> resources);
    //
    // Task<int> Count(Expression<Func<Category, bool>> selector);
    // Task<bool> Any(Expression<Func<Category, bool>> selector);
    // Task<BaseResponse> Update(Category resource);
    // Task<BaseResponse> UpdateRange(IEnumerable<Category> resources);
    //
    // Task<SingletonResponse<Category>> UpdateFirst(Expression<Func<Category, bool>> selector,
    //     Action<Category> modify);
    //
    // Task<ListResponse<Category>> UpdateAll(Expression<Func<Category, bool>> selector,
    //     Action<Category> modify);
    //
    Task<List<Category>> GetByKeys(IEnumerable<Int32> keys, CategoryAdditionalItem additionalItems);
    //
    // Task<SingletonResponse<Category>> UpdateByKey(Int32 key, Action<Category> modify);
    //
    // Task<ListResponse<Category>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Category> modify);
    //
    // /// <summary>
    // /// 删除
    // /// </summary>
    // /// <param name="key"></param>
    // /// <returns></returns>
    // Task<BaseResponse> RemoveByKey(Int32 key);
    //
    // /// <summary>
    // /// 删除
    // /// </summary>
    // /// <param name="keys"></param>
    // /// <returns></returns>
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
}