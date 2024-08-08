using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Enhancer.Abstractions.Services
{
    public interface ICategoryEnhancerOptionsService
    {
        Task<CategoryEnhancerFullOptions?> GetByCategoryAndEnhancer(int cId, int eId);
        public Task<List<CategoryEnhancerFullOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>>? exp);
        public Task<SingletonResponse<CategoryEnhancerFullOptions>> Patch(int categoryId, int enhancerId, CategoryEnhancerOptionsPatchInputModel model);
        public Task<BaseResponse> PatchTarget(int categoryId, int enhancerId, int target, string? dynamicTarget, CategoryEnhancerTargetOptionsPatchInputModel patches);
        public Task<BaseResponse> DeleteTarget(int categoryId, int enhancerId, int target, string? dynamicTarget);
        public Task<BaseResponse> PutAll(CategoryEnhancerFullOptions[] options);

        // /// <summary>
        // /// 获取全部默认资源
        // /// </summary>
        // /// <param name="selector">为空则获取全部</param>
        // /// <param name="asNoTracking"></param>
        // /// <returns></returns>
        // Task<List<CategoryEnhancerOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>> selector = null, bool asNoTracking = false);
        //
        Task<List<CategoryEnhancerFullOptions>> GetByCategory(int categoryId);
        //
        //
        // /// <summary>
        // /// 获取单条默认资源
        // /// </summary>
        // /// <param name="selector"></param>
        // /// <param name="orderBy"></param>
        // /// <param name="asc"></param>
        // /// <param name="asNoTracking"></param>
        // /// <returns></returns>
        // Task<CategoryEnhancerOptions> GetFirst(Expression<Func<CategoryEnhancerOptions, bool>> selector,
        //     Expression<Func<CategoryEnhancerOptions, object>> orderBy = null,
        //     bool asc = false, bool asNoTracking = false);
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
        // Task<SearchResponse<CategoryEnhancerOptions>> Search(
        //     Expression<Func<CategoryEnhancerOptions, bool>> selector, int pageIndex, int pageSize,
        //     Expression<Func<CategoryEnhancerOptions, object>> orderBy = null, bool asc = false,
        //     Expression<Func<CategoryEnhancerOptions, object>> include = null);
        //
        // Task<BaseResponse> Remove(CategoryEnhancerOptions resource);
        // Task<BaseResponse> RemoveRange(IEnumerable<CategoryEnhancerOptions> resources);
        //
        // /// <summary>
        // /// 删除默认资源
        // /// </summary>
        // /// <param name="selector"></param>
        // /// <returns></returns>
        // Task<BaseResponse> RemoveAll(Expression<Func<CategoryEnhancerOptions, bool>> selector);
        //
        // /// <summary>
        // /// 创建默认资源
        // /// </summary>
        // /// <param name="resource"></param>
        // /// <returns></returns>
        // Task<SingletonResponse<CategoryEnhancerOptions>> Add(CategoryEnhancerOptions resource);
        //
        // /// <summary>
        // /// 创建默认资源
        // /// </summary>
        // /// <param name="resources"></param>
        // /// <returns></returns>
        // Task<ListResponse<CategoryEnhancerOptions>> AddRange(IEnumerable<CategoryEnhancerOptions> resources);
        //
        // Task<int> Count(Expression<Func<CategoryEnhancerOptions, bool>> selector);
        // Task<bool> Any(Expression<Func<CategoryEnhancerOptions, bool>> selector);
        // Task<BaseResponse> Update(CategoryEnhancerOptions resource);
        // Task<BaseResponse> UpdateRange(IEnumerable<CategoryEnhancerOptions> resources);
        //
        // Task<SingletonResponse<CategoryEnhancerOptions>> UpdateFirst(Expression<Func<CategoryEnhancerOptions, bool>> selector,
        //     Action<CategoryEnhancerOptions> modify);
        //
        // Task<ListResponse<CategoryEnhancerOptions>> UpdateAll(Expression<Func<CategoryEnhancerOptions, bool>> selector,
        //     Action<CategoryEnhancerOptions> modify);
        //
        // /// <summary>
        // /// 
        // /// </summary>
        // /// <param name="key"></param>
        // /// <param name="asNoTracking"></param>
        // /// <returns></returns>
        // Task<CategoryEnhancerOptions> GetByKey(Int32 key, bool asNoTracking = false);
        //
        // /// <summary>
        // /// 
        // /// </summary>
        // /// <param name="keys"></param>
        // /// <returns></returns>
        // Task<List<CategoryEnhancerOptions>> GetByKeys(IEnumerable<Int32> keys);
        //
        // Task<SingletonResponse<CategoryEnhancerOptions>> UpdateByKey(Int32 key, Action<CategoryEnhancerOptions> modify);
        //
        // Task<ListResponse<CategoryEnhancerOptions>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
        //     Action<CategoryEnhancerOptions> modify);
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
}