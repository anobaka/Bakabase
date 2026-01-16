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
    Task DeleteByKeys(int[] ids);

    Task<List<Resource>> GetAll(Expression<Func<Models.Db.ResourceDbModel, bool>>? selector = null,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);
    
    Task<SearchResponse<Resource>> Search(ResourceSearch model,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.All,
        bool asNoTracking = true);

    /// <summary>
    /// Lightweight search that returns only resource IDs without loading additional items.
    /// Use this to avoid circular dependencies (e.g., ResourceProfile matching).
    /// </summary>
    Task<int[]> GetAllIds(ResourceSearch model);

    /// <summary>
    /// Returns all resource IDs without any filtering or additional items.
    /// Ultra-lightweight method for preventing circular dependencies.
    /// </summary>
    Task<int[]> GetAllResourceIds();

    Task<Abstractions.Models.Domain.Resource?> Get(int id,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    Task<List<Abstractions.Models.Domain.Resource>> GetByKeys(int[] ids,
        ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

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
    Task<string[]> DiscoverAndCachePlayableFiles(int id, CancellationToken ct);

    Task<bool> Any(Func<Abstractions.Models.Db.ResourceDbModel, bool>? selector = null);

    Task<List<Abstractions.Models.Db.ResourceDbModel>> AddAll(
        IEnumerable<Abstractions.Models.Db.ResourceDbModel> resources);

    Task<BaseResponse> PutPropertyValue(int resourceId, ResourcePropertyValuePutInputModel model);

    /// <summary>
    /// Bulk update property values for multiple resources in a single batch operation.
    /// More efficient than calling PutPropertyValue multiple times.
    /// </summary>
    Task<BaseResponse> BulkPutPropertyValue(int[] resourceIds, ResourcePropertyValuePutInputModel model);

    /// <summary>
    /// Raw cover, no cache.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    /// <returns>File path</returns>
    Task<string?> DiscoverAndCacheCover(int id, CancellationToken ct);

    Task<BaseResponse> Play(int resourceId, string file);

    Task<BaseResponse> ChangeMediaLibrary(int[] ids, int mediaLibraryId, Dictionary<int, string>? newPaths = null);
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
    Task DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId, ResourceCacheType type);
    Task DeleteResourceCacheByResourceIdsAndCacheType(IEnumerable<int> resourceIds, ResourceCacheType type);
    Task DeleteUnassociatedResourceCacheByCacheType(ResourceCacheType type);

    Task MarkAsNotPlayed(int id);

    Task<Resource[]> GetAllGeneratedByMediaLibraryV2(int[]? ids = null, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    /// <summary>
    /// Get resources by media library ID using MediaLibraryResourceMapping
    /// </summary>
    /// <param name="mediaLibraryId">Media library ID</param>
    /// <param name="additionalItems">Additional items to include</param>
    /// <returns>List of resources associated with the media library</returns>
    Task<List<Resource>> GetByMediaLibraryId(int mediaLibraryId, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None);

    /// <summary>
    /// Set media libraries for resources, replacing existing mappings.
    /// </summary>
    /// <param name="resourceIds">Resource IDs to update</param>
    /// <param name="mediaLibraryIds">Media library IDs to set</param>
    /// <returns>Response indicating success or failure</returns>
    Task<BaseResponse> SetMediaLibraries(int[] resourceIds, int[] mediaLibraryIds);

    Segment[] BuildDisplayNameSegmentsForResource(Resource resource, string template,
        (string Left, string Right)[] wrappers);

    string BuildDisplayNameForResource(Resource resource, string template, (string Left, string Right)[] wrappers);

    /// <summary>
    /// Play a random resource that has playable files cached.
    /// </summary>
    Task<BaseResponse> PlayRandomResource();

    /// <summary>
    /// Gets the hierarchy context for a resource, including ancestors and children count.
    /// </summary>
    /// <param name="resourceId">The resource ID</param>
    /// <returns>A tuple containing the list of ancestors (from root to immediate parent) and the count of children</returns>
    Task<(List<Resource> Ancestors, int ChildrenCount)> GetHierarchyContext(int resourceId);
}