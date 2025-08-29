using System.Linq.Expressions;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using static Bakabase.Abstractions.Models.View.ResourceDisplayNameViewModel;

namespace Bakabase.Abstractions.Services;

public interface IResourceService
{
    // Task RemoveByMediaLibraryIdsNotIn(int[] ids);
    Task DeleteByKeys(int[] ids, bool deleteFiles);

    // Task LogicallyRemoveByCategoryId(int categoryId);
    Task<List<Resource>> GetAll(Expression<Func<Models.Db.ResourceDbModel, bool>>? selector = null,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    //
    // Task<List<Abstractions.Models.Db.Resource>> GetAll(Expression<Func<Abstractions.Models.Db.Resource, bool>> selector = null,
    //     bool asNoTracking = true);
    //
    Task<SearchResponse<Resource>> Search(ResourceSearch model);
    Task<int[]> GetAllIds(ResourceSearch model);

    // Task<Abstractions.Models.Db.Resource?> GetByKey(int id, bool asNoTracking);
    Task<Abstractions.Models.Domain.Resource?> Get(int id,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    Task<List<Abstractions.Models.Domain.Resource>> GetByKeys(int[] ids,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    //
    // Task<Resource> ToDomainModel(Abstractions.Models.Db.Resource resource,
    //     ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);
    //
    // Task<List<Resource>> ToDomainModel(Abstractions.Models.Db.Resource[] resources,
    //     ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);
    //
    Task<List<Abstractions.Models.Db.ResourceDbModel>> GetAllDbModels(
        Expression<Func<Abstractions.Models.Db.ResourceDbModel, bool>>? selector = null,
        bool asNoTracking = true);

    //
    /// <summary>
    /// <para>All properties of resources will be saved, including null values.</para>
    /// <para>Parents will be saved too, so be sure the properties of parent are fulfilled also.</para>
    /// </summary>
    /// <param name="resources"></param>
    /// <returns></returns>
    Task<List<DataChangeViewModel>> AddOrPutRange(List<Abstractions.Models.Domain.Resource> resources);

    Task RefreshParentTag();
    Task<string[]> GetPlayableFiles(int id, CancellationToken ct);

    Task<bool> Any(Func<Abstractions.Models.Db.ResourceDbModel, bool>? selector = null);

    Task<List<Abstractions.Models.Db.ResourceDbModel>> AddAll(
        IEnumerable<Abstractions.Models.Db.ResourceDbModel> resources);

    Task<BaseResponse> PutPropertyValue(int resourceId, ResourcePropertyValuePutInputModel model);

    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="path"></param>
    // /// <param name="ct"></param>
    // /// <param name="order"></param>
    // /// <param name="additionalSources"></param>
    // /// <returns>If ShouldSave is true, it usually means the cost of discovering cover is high, and we should save the result for better performance.</returns>
    // /// <exception cref="ArgumentOutOfRangeException"></exception>
    // Task<(Stream Stream, string Ext, bool ShouldSave)?> DiscoverCover(string path,
    //     CancellationToken ct, CoverSelectOrder order,
    //     AdditionalCoverDiscoveringSource[] additionalSources);
    //
    // Task<List<Resource>> GetNfoGenerationNeededResources(int[] resourceIds);
    // Task SaveNfo(Resource resource, bool overwrite, CancellationToken ct = new());
    // Task TryToGenerateNfoInBackground();
    // Task RunBatchSaveNfoBackgroundTask(int[] resourceIds, string backgroundTaskName, bool overwrite);
    // Task<BaseResponse> StartGeneratingNfo(BackgroundTask task);
    // Task PopulateStatistics(DashboardStatistics statistics);
    //
    // Task<BaseResponse> SaveThumbnail(int id, bool overwrite, byte[] imageBytes, CancellationToken ct);

    /// <summary>
    /// Raw cover, no cache.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    /// <returns>File path</returns>
    Task<string?> DiscoverAndCacheCover(int id, CancellationToken ct);

    Task<BaseResponse> Play(int resourceId, string file);
    Task<List<Resource>> GetUnknownResources();
    Task<int> GetUnknownCount();
    Task DeleteUnknown();

    Task<BaseResponse> ChangeMediaLibrary(int[] ids, int mediaLibraryId, bool isLegacyMediaLibrary = false, Dictionary<int, string>? newPaths = null);
    Task<BaseResponse> ChangePath(int[] ids, Dictionary<int, string> newPaths);

    Task Pin(int id, bool pin);

    Task PrepareCache(Func<int, Task>? onProgressChange, Func<string, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct);
    Task Transfer(ResourceTransferInputModel model);
    Task SaveCover(int id, byte[] imageBytes, CoverSaveMode mode);

    /// <summary>
    /// CacheType - Data
    /// </summary>
    /// <returns></returns>
    Task<CacheOverviewViewModel> GetCacheOverview();
    Task<ResourceCache?> GetResourceCache(int id);

    Task DeleteResourceCacheByResourceIdAndCacheType(int resourceId, ResourceCacheType type);
    Task DeleteResourceCacheByCategoryIdAndCacheType(int categoryId, ResourceCacheType type);
    Task DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId, ResourceCacheType type);

    Task MarkAsNotPlayed(int id);

    Task<Resource[]> GetAllGeneratedByMediaLibraryV2(int[]? ids = null, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    Segment[] BuildDisplayNameSegmentsForResource(Resource resource, string template,
        (string Left, string Right)[] wrappers);

    string BuildDisplayNameForResource(Resource resource, string template, (string Left, string Right)[] wrappers);
}