using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Abstractions.Services;

[Obsolete]
public interface IMediaLibraryService
{
    Task<BaseResponse> Add(MediaLibraryAddDto model);
    //
    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resource"></param>
    // /// <returns></returns>
    // Task<SingletonResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> Add(InsideWorld.Models.Models.Entities.MediaLibrary resource);
    //
    Task AddRange(ICollection<MediaLibrary> mls);
    //
    // /// <summary>
    // /// 创建默认资源
    // /// </summary>
    // /// <param name="resources"></param>
    // /// <returns></returns>
    // Task<ListResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> AddRange(IEnumerable<InsideWorld.Models.Models.Entities.MediaLibrary> resources);
    //
    Task<BaseResponse> Patch(int id, MediaLibraryPatchDto model);
    Task<BaseResponse> Put(MediaLibrary dto);
    Task<MediaLibrary?> Get(int id, MediaLibraryAdditionalItem additionalItems = MediaLibraryAdditionalItem.None);
    //
    Task<List<MediaLibrary>> GetAll(Expression<Func<Models.Db.MediaLibraryDbModel, bool>>? exp = null,
        MediaLibraryAdditionalItem additionalItems = MediaLibraryAdditionalItem.None);
    //
    Task<BaseResponse> Sort(int[] ids);
    Task<BaseResponse> DuplicateAllInCategory(int fromCategoryId, int toCategoryId);
    void StartSyncing(int[]? categoryIds, int[]? mediaLibraryIds);

    Task<SingletonResponse<SyncResultViewModel>> Sync(int[]? categoryIds, int[]? mediaLibraryIds, Action<string> onProcessChange, Action<int> onProgressChange);

    Task<SingletonResponse<PathConfigurationTestResult>>
        Test(PathConfiguration pc, int maxResourceCount = int.MaxValue);
    //
    // /// <summary>
    // /// 获取单条默认资源
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="orderBy"></param>
    // /// <param name="asc"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<InsideWorld.Models.Models.Entities.MediaLibrary> GetFirst(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector,
    //     Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, object>> orderBy = null,
    //     bool asc = false, bool asNoTracking = false);
    //
    // /// <summary>
    // /// 获取全部默认资源
    // /// </summary>
    // /// <param name="selector">为空则获取全部</param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<List<InsideWorld.Models.Models.Entities.MediaLibrary>> GetAll(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector = null, bool asNoTracking = false);
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
    // Task<SearchResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> Search(
    //     Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector, int pageIndex, int pageSize,
    //     Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, object>> orderBy = null, bool asc = false,
    //     Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, object>> include = null);
    //
    // Task<BaseResponse> Remove(InsideWorld.Models.Models.Entities.MediaLibrary resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<InsideWorld.Models.Models.Entities.MediaLibrary> resources);
    
    Task<BaseResponse> DeleteAll(Expression<Func<Models.Db.MediaLibraryDbModel, bool>> selector);
    //
    // Task<int> Count(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector);
    // Task<bool> Any(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector);
    // Task<BaseResponse> Update(InsideWorld.Models.Models.Entities.MediaLibrary resource);
    // Task<BaseResponse> UpdateRange(IEnumerable<InsideWorld.Models.Models.Entities.MediaLibrary> resources);
    //
    // Task<SingletonResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> UpdateFirst(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector,
    //     Action<InsideWorld.Models.Models.Entities.MediaLibrary> modify);
    //
    // Task<ListResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> UpdateAll(Expression<Func<InsideWorld.Models.Models.Entities.MediaLibrary, bool>> selector,
    //     Action<InsideWorld.Models.Models.Entities.MediaLibrary> modify);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="key"></param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<InsideWorld.Models.Models.Entities.MediaLibrary> GetByKey(Int32 key, bool asNoTracking = false);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="keys"></param>
    // /// <returns></returns>
    // Task<List<InsideWorld.Models.Models.Entities.MediaLibrary>> GetByKeys(IEnumerable<Int32> keys);
    //
    // Task<SingletonResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> UpdateByKey(Int32 key, Action<InsideWorld.Models.Models.Entities.MediaLibrary> modify);
    //
    // Task<ListResponse<InsideWorld.Models.Models.Entities.MediaLibrary>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<InsideWorld.Models.Models.Entities.MediaLibrary> modify);
    //
    /// <summary>
    /// 删除
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    Task<BaseResponse> DeleteByKey(Int32 key);
    //
    // /// <summary>
    // /// 删除
    // /// </summary>
    // /// <param name="keys"></param>
    // /// <returns></returns>
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
}