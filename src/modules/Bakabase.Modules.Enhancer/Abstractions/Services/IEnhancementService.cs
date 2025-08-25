using System.Linq.Expressions;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancementService
{
    Task<List<Bakabase.Abstractions.Models.Domain.Enhancement>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementDbModel, bool>>? exp, EnhancementAdditionalItem additionalItem = EnhancementAdditionalItem.None);

    // /// <summary>
    // /// 获取全部默认资源
    // /// </summary>
    // /// <param name="selector">为空则获取全部</param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<List<Bakabase.Abstractions.Models.Db.Enhancement>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector = null, bool asNoTracking = false);

    Task AddRange(List<Bakabase.Abstractions.Models.Domain.Enhancement> enhancements);

    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resources"></param>
    // /// <returns></returns>
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.Enhancement>> AddRange(IEnumerable<Bakabase.Abstractions.Models.Db.Enhancement> resources);
    //
    Task UpdateRange(List<Bakabase.Abstractions.Models.Domain.Enhancement> enhancements);
    // Task<BaseResponse> UpdateRange(IEnumerable<Bakabase.Abstractions.Models.Db.Enhancement> resources);
    // InsideWorldDbContext DbContext { get; }
    //
    // /// <summary>
    // /// 获取单条默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="orderBy"></param>
    // /// <param name="asc"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<Bakabase.Abstractions.Models.Db.Enhancement> GetFirst(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector,
    //     Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, object>> orderBy = null,
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
    // Task<SearchResponse<Bakabase.Abstractions.Models.Db.Enhancement>> Search(
    //     Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector, int pageIndex, int pageSize,
    //     Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, object>> orderBy = null, bool asc = false,
    //     Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, object>> include = null);
    //
    // Task<BaseResponse> Remove(Bakabase.Abstractions.Models.Db.Enhancement resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Bakabase.Abstractions.Models.Db.Enhancement> resources);
    //
    // /// <summary>
    // /// 删除默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <returns></returns>
    // Task<BaseResponse> RemoveAll(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector);
    Task<BaseResponse> RemoveAll(Expression<Func<Bakabase.Abstractions.Models.Db.EnhancementDbModel, bool>> selector, bool removeGeneratedCustomPropertyValues);
    //
    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resource"></param>
    // /// <returns></returns>
    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.Enhancement>> Add(Bakabase.Abstractions.Models.Db.Enhancement resource);
    //
    // Task<int> Count(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector);
    // Task<bool> Any(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector);
    // Task<BaseResponse> Update(Bakabase.Abstractions.Models.Db.Enhancement resource);
    //
    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.Enhancement>> UpdateFirst(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector,
    //     Action<Bakabase.Abstractions.Models.Db.Enhancement> modify);
    //
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.Enhancement>> UpdateAll(Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector,
    //     Action<Bakabase.Abstractions.Models.Db.Enhancement> modify);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="key"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<Bakabase.Abstractions.Models.Db.Enhancement> GetByKey(Int32 key, bool asNoTracking = false);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="keys"></param>
    // /// <returns></returns>
    // Task<List<Bakabase.Abstractions.Models.Db.Enhancement>> GetByKeys(IEnumerable<Int32> keys);
    //
    // Task<SingletonResponse<Bakabase.Abstractions.Models.Db.Enhancement>> UpdateByKey(Int32 key, Action<Bakabase.Abstractions.Models.Db.Enhancement> modify);
    //
    // Task<ListResponse<Bakabase.Abstractions.Models.Db.Enhancement>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Bakabase.Abstractions.Models.Db.Enhancement> modify);
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