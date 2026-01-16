using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.Property.Services;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Cryptography;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using DotNext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NPOI.Util.Collections;
using StackExchange.Profiling;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/resource")]
    public class ResourceController(
        IResourceService service,
        FfMpegService ffMpegService,
        IBOptionsManager<ResourceOptions> resourceOptionsManager,
        FfMpegService ffMpegInstaller,
        ILogger<ResourceController> logger,
        IPropertyService propertyService,
        ICustomPropertyService customPropertyService,
        ICustomPropertyValueService customPropertyValueService,
        IPropertyLocalizer propertyLocalizer,
        IMediaLibraryService mediaLibraryService,
        IMediaLibraryV2Service mediaLibraryV2Service,
        IMediaLibraryResourceMappingService mappingService,
        IBakabaseLocalizer localizer,
        BTaskManager taskManager,
        IBOptionsManager<FileSystemOptions> fsOptionsManager)
        : Controller
    {
        [HttpGet("search-operation")]
        [SwaggerOperation(OperationId = "GetSearchOperationsForProperty")]
        public async Task<ListResponse<SearchOperation>> GetSearchOperationsForProperty(
            PropertyPool propertyPool, int propertyId)
        {
            PropertyType? pt;
            if (propertyPool != PropertyPool.Custom)
            {
                pt = PropertySystem.Builtin.TryGet((ResourceProperty) propertyId)?.Type;
            }
            else
            {
                var p = await customPropertyService.GetByKey(propertyId);
                pt = p.Type;
            }

            if (!pt.HasValue)
            {
                return ListResponseBuilder<SearchOperation>.NotFound;
            }

            var psh = PropertySystem.Property.TryGetSearchHandler(pt.Value);

            return new ListResponse<SearchOperation>(psh?.SearchOperations.Keys);
        }

        [HttpGet("filter-value-property")]
        [SwaggerOperation(OperationId = "GetFilterValueProperty")]
        public async Task<SingletonResponse<PropertyViewModel>> GetFilterValueProperty(PropertyPool propertyPool,
            int propertyId,
            SearchOperation operation)
        {
            var p = await propertyService.GetProperty(propertyPool, propertyId);

            var psh = PropertySystem.Property.TryGetSearchHandler(p.Type);
            if (psh?.SearchOperations.TryGetValue(operation, out var options) != true)
            {
                return SingletonResponseBuilder<PropertyViewModel>.NotFound;
            }

            if (options?.ConvertProperty != null)
            {
                p = options.ConvertProperty(p);
            }

            return new SingletonResponse<PropertyViewModel>(p.ToViewModel(propertyLocalizer));
        }

        [HttpGet("last-search")]
        [SwaggerOperation(OperationId = "GetLastResourceSearch")]
        public async Task<SingletonResponse<ResourceSearchViewModel?>> GetLastResourceSearch()
        {
            var ls = resourceOptionsManager.Value.LastSearchV2;
            if (ls == null)
            {
                return new SingletonResponse<ResourceSearchViewModel?>(null);
            }

            ResourceSearchDbModel[] arr = [ls];
            var viewModels = await arr.ToViewModels(propertyService, propertyLocalizer, service);
            return new SingletonResponse<ResourceSearchViewModel?>(viewModels[0]);
        }

        [HttpPost("saved-search")]
        [SwaggerOperation(OperationId = "SaveNewResourceSearch")]
        public async Task<SingletonResponse<SavedSearchViewModel>> SaveNewSearch([FromBody] SavedSearchAddInputModel model)
        {
            model.Search.StandardPageable();
            var ss = resourceOptionsManager.Value.BuildNewSavedSearch(null, localizer.Search(), model.Search.ToDbModel(), model.DisplayMode);
            await resourceOptionsManager.SaveAsync(x =>
            {
                x.SavedSearches.Add(ss);
            });
            return await GetSavedSearch(ss.Id);
        }

        [HttpPut("saved-search")]
        [SwaggerOperation(OperationId = "PutSavedSearchName")]
        public async Task<BaseResponse> PutSavedSearchName(string id, [FromBody] string name)
        {
            var searches = resourceOptionsManager.Value.SavedSearches;
            var search = searches.FirstOrDefault(x => x.Id == id);
            if (search == null)
            {
                return BaseResponseBuilder.BuildBadRequest($"Can't find search with id: {id}");
            }

            search.Name = name;
            await resourceOptionsManager.SaveAsync(x => { x.SavedSearches = searches; });
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("saved-search/display-mode")]
        [SwaggerOperation(OperationId = "PutSavedSearchDisplayMode")]
        public async Task<BaseResponse> PutSavedSearchDisplayMode(string id, [FromBody] FilterDisplayMode displayMode)
        {
            var searches = resourceOptionsManager.Value.SavedSearches;
            var search = searches.FirstOrDefault(x => x.Id == id);
            if (search == null)
            {
                return BaseResponseBuilder.BuildBadRequest($"Can't find search with id: {id}");
            }

            search.DisplayMode = displayMode;
            await resourceOptionsManager.SaveAsync(x => { x.SavedSearches = searches; });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("saved-search")]
        [SwaggerOperation(OperationId = "GetSavedSearch")]
        public async Task<SingletonResponse<SavedSearchViewModel>> GetSavedSearch(string id)
        {
            var searches = resourceOptionsManager.Value.SavedSearches;
            var search = searches.FirstOrDefault(x => x.Id == id);
            if (search == null)
            {
                return SingletonResponseBuilder<SavedSearchViewModel>.NotFound;
            }
                
            var dbModels = new[] { search.Search };
            var viewModels = await dbModels.ToViewModels(propertyService, propertyLocalizer, service);
            return new SingletonResponse<SavedSearchViewModel>(new SavedSearchViewModel(id, viewModels[0], search.Name, search.DisplayMode));
        }

        [HttpDelete("saved-search")]
        [SwaggerOperation(OperationId = "DeleteSavedSearch")]
        public async Task<BaseResponse> DeleteSavedSearch(string id)
        {
            await resourceOptionsManager.SaveAsync(x =>
            {
                x.SavedSearches.RemoveAll(z => z.Id == id);
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("search")]
        [SwaggerOperation(OperationId = "SearchResources")]
        public async Task<SearchResponse<Resource>> Search([FromBody] ResourceSearchInputModel model, bool saveSearch, string? searchId = null, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.All)
        {
            using (MiniProfiler.Current.Step("StandardPageable"))
            {
                model.StandardPageable();
            }

            if (saveSearch)
            {
                // Fire-and-forget: save search criteria in background to avoid blocking search response
                var dbModel = model.ToDbModel();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await resourceOptionsManager.SaveAsync(a =>
                        {
                            a.LastSearchV2 = dbModel;
                            if (searchId.IsNotEmpty())
                            {
                                var search = a.SavedSearches.FirstOrDefault(x => x.Id == searchId);
                                if (search != null)
                                {
                                    search.Search = dbModel;
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to save search criteria");
                    }
                });
            }

            using (MiniProfiler.Current.Step("InputModel.ToDomainModel"))
            {
                var domainModel = await model.ToDomainModel(propertyService);

                using (MiniProfiler.Current.Step("ResourceService.Search"))
                {
                    return await service.Search(domainModel, additionalItems);
                }
            }
        }

        [HttpPost("search/ids")]
        [SwaggerOperation(OperationId = "SearchAllResourceIds")]
        public async Task<ListResponse<int>> SearchAllIds([FromBody] ResourceSearchInputModel model)
        {
            model.StandardPageable();

            var domainModel = await model.ToDomainModel(propertyService);

            return new ListResponse<int>(await service.GetAllIds(domainModel));
        }

        [HttpGet("keys")]
        [SwaggerOperation(OperationId = "GetResourcesByKeys")]
        public async Task<ListResponse<Resource>> GetByKeys([FromQuery] int[] ids,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            return new ListResponse<Resource>(await service.GetByKeys(ids, additionalItems));
        }

        [HttpGet("{id:int}/hierarchy-context")]
        [SwaggerOperation(OperationId = "GetResourceHierarchyContext")]
        public async Task<SingletonResponse<ResourceHierarchyContextViewModel>> GetHierarchyContext(int id)
        {
            var (ancestors, childrenCount) = await service.GetHierarchyContext(id);
            var ancestorViewModels = ancestors.Select(a => new ResourceAncestorViewModel(
                a.Id,
                a.DisplayName,
                a.ParentId
            )).ToList();

            return new SingletonResponse<ResourceHierarchyContextViewModel>(
                new ResourceHierarchyContextViewModel(ancestorViewModels, childrenCount > 0 ? childrenCount : null));
        }

        // [HttpPut("{id}")]
        // [SwaggerOperation(OperationId = "PatchResource")]
        // public async Task<BaseResponse> Update(int id, [FromBody] ResourceUpdateRequestModel model)
        // {
        // 	return await _service.Patch(id, model);
        // }

        [HttpGet("directory")]
        [SwaggerOperation(OperationId = "OpenResourceDirectory")]
        public async Task<BaseResponse> Open(int id)
        {
            var resource = await service.Get(id, ResourceAdditionalItem.None);
            var rawFileOrDirectoryName = Path.Combine(resource.Path);
            // https://github.com/Bakabase/InsideWorld/issues/51
            if (!System.IO.File.Exists(rawFileOrDirectoryName) && !Directory.Exists(rawFileOrDirectoryName))
            {
                rawFileOrDirectoryName = resource.Directory;
            }

            var rawAttributes = System.IO.File.GetAttributes(rawFileOrDirectoryName);
            FileService.Open(rawFileOrDirectoryName,
                (rawAttributes & FileAttributes.Directory) != FileAttributes.Directory);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("{id}/cover")]
        [SwaggerOperation(OperationId = "DiscoverResourceCover")]
        public async Task<IActionResult> DiscoverCover(int id)
        {
            var coverPath = await service.DiscoverAndCacheCover(id, HttpContext.RequestAborted);
            if (coverPath.IsNullOrEmpty())
            {
                var resource = await service.Get(id, ResourceAdditionalItem.None);
                if (resource == null)
                {
                    return NotFound();
                }

                coverPath = resource.Path;
            }

            return RedirectToAction("GetThumbnail", "Tool", new {path = coverPath});
        }

        [HttpGet("{id}/playable-files")]
        [SwaggerOperation(OperationId = "GetResourcePlayableFiles")]
        [ResponseCache(Duration = 20 * 60)]
        public async Task<ListResponse<string>> GetPlayableFiles(int id)
        {
            return new ListResponse<string>(await service.DiscoverAndCachePlayableFiles(id, HttpContext.RequestAborted) ??
                                            new string[] { });
        }

        [HttpPut("media-libraries")]
        [SwaggerOperation(OperationId = "SetResourceMediaLibraries")]
        public async Task<BaseResponse> SetMediaLibraries([FromBody] ResourceSetMediaLibrariesRequestModel model)
        {
            return await service.SetMediaLibraries(model.Ids, model.MediaLibraryIds);
        }

        [HttpPost("media-library-mappings")]
        [SwaggerOperation(OperationId = "GetResourceMediaLibraryMappings")]
        public async Task<SingletonResponse<Dictionary<int, int[]>>> GetMediaLibraryMappings([FromBody] int[] resourceIds)
        {
            var mappings = await mappingService.GetByResourceIds(resourceIds);
            var result = mappings
                .GroupBy(m => m.ResourceId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.MediaLibraryId).ToArray());

            // Ensure all requested resource IDs are in the result (even if they have no mappings)
            foreach (var id in resourceIds)
            {
                if (!result.ContainsKey(id))
                {
                    result[id] = Array.Empty<int>();
                }
            }

            return new SingletonResponse<Dictionary<int, int[]>>(result);
        }

        // [HttpPost("nfo")]
        // [SwaggerOperation(OperationId = "StartResourceNfoGenerationTask")]
        // public async Task<BaseResponse> StartNfoGenerationTask()
        // {
        // 	await _service.TryToGenerateNfoInBackground();
        // 	return BaseResponseBuilder.Ok;
        // }

        [HttpGet("{id}/previewer")]
        [SwaggerOperation(OperationId = "GetResourceDataForPreviewer")]
        public async Task<ListResponse<PreviewerItem>> GetResourceDataForPreviewer(int id)
        {
            var resource = await service.Get(id, ResourceAdditionalItem.None);

            if (resource == null)
            {
                return ListResponseBuilder<PreviewerItem>.NotFound;
            }

            var filePaths = new List<string>();
            if (System.IO.File.Exists(resource.Path))
            {
                filePaths.Add(resource.Path);
            }
            else
            {
                if (Directory.Exists(resource.Path))
                {
                    filePaths.AddRange(Directory.GetFiles(resource.Path, "*", SearchOption.AllDirectories));
                }
                else
                {
                    return ListResponseBuilder<PreviewerItem>.NotFound;
                }
            }

            var items = new List<PreviewerItem>();
            var ffmpegIsReady = ffMpegInstaller.Status == DependentComponentStatus.Installed;

            foreach (var f in filePaths)
            {
                var type = f.InferMediaType();
                switch (type)
                {
                    case MediaType.Image:
                        items.Add(new PreviewerItem
                        {
                            Duration = 1,
                            FilePath = f.StandardizePath()!,
                            Type = type
                        });
                        break;
                    case MediaType.Video:
                        if (ffmpegIsReady)
                        {
                            items.Add(new PreviewerItem
                            {
                                Duration = (int) Math.Ceiling(
                                    (await ffMpegService.GetDuration(f, HttpContext.RequestAborted))),
                                FilePath = f.StandardizePath()!,
                                Type = type
                            });
                        }

                        break;
                    case MediaType.Text:
                    case MediaType.Audio:
                    case MediaType.Unknown:
                        // Not available for previewing
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return new ListResponse<PreviewerItem>(items);
        }

        [HttpPut("{id:int}/property-value")]
        [SwaggerOperation(OperationId = "PutResourcePropertyValue")]
        public async Task<BaseResponse> PutPropertyValue(int id, [FromBody] ResourcePropertyValuePutInputModel model)
        {
            return await service.PutPropertyValue(id, model);
        }

        /// <summary>
        /// Bulk update property values across multiple resources using batch operations
        /// </summary>
        [HttpPut("bulk/property-value")]
        [SwaggerOperation(OperationId = "BulkPutResourcePropertyValue")]
        public async Task<BaseResponse> BulkPutPropertyValue([FromBody] BulkResourcePropertyValuePutInputModel model)
        {
            if (model.ResourceIds.Count == 0)
            {
                return BaseResponseBuilder.BuildBadRequest("No resource IDs provided");
            }

            var propertyValueModel = new ResourcePropertyValuePutInputModel
            {
                PropertyId = model.PropertyId,
                IsCustomProperty = model.IsCustomProperty,
                Value = model.Value
            };

            return await service.BulkPutPropertyValue(model.ResourceIds.ToArray(), propertyValueModel);
        }

        [HttpGet("{resourceId}/play")]
        [SwaggerOperation(OperationId = "PlayResourceFile")]
        public async Task<BaseResponse> Play(int resourceId, string? file)
        {
            if (file.IsNullOrEmpty())
            {
                var pfs = await service.DiscoverAndCachePlayableFiles(resourceId, HttpContext.RequestAborted);
                file = pfs.FirstOrDefault();
            }

            if (file.IsNullOrEmpty())
            {
                return BaseResponseBuilder.BuildBadRequest("No playable file was found.");
            }

            return await service.Play(resourceId, file);
        }

        [HttpGet("play/random")]
        [SwaggerOperation(OperationId = "PlayRandomResource")]
        public async Task<BaseResponse> PlayRandom()
        {
            return await service.PlayRandomResource();
        }

        [HttpDelete("ids")]
        [SwaggerOperation(OperationId = "DeleteResourcesByKeys")]
        public async Task<BaseResponse> DeleteByKeys(int[] ids)
        {
            await service.DeleteByKeys(ids);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("{id:int}/pin")]
        [SwaggerOperation(OperationId = "PinResource")]
        public async Task<BaseResponse> Pin(int id, bool pin)
        {
            await service.Pin(id, pin);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("transfer")]
        [SwaggerOperation(OperationId = "TransferResourceData")]
        public async Task<BaseResponse> Transfer([FromBody] ResourceTransferInputModel model)
        {
            await service.Transfer(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("paths")]
        [SwaggerOperation(OperationId = "SearchResourcePaths")]
        public async Task<ListResponse<ResourcePathInfoViewModel>> SearchPaths(string keyword)
        {
            var resources =
                await service.GetAll(x => x.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase),
                    ResourceAdditionalItem.None);
            var viewModels = resources.Select(r => new ResourcePathInfoViewModel(r.Id, r.Path, r.FileName));

            return new ListResponse<ResourcePathInfoViewModel>(viewModels);
        }

        [HttpPut("{id:int}/cover")]
        [SwaggerOperation(OperationId = "SaveCover")]
        public async Task<BaseResponse> SaveCover(int id, [FromBody] ResourceCoverSaveInputModel model)
        {
            var data = Convert.FromBase64String(model.Base64String.Split(',')[1]);
            await service.SaveCover(id, data, model.SaveMode);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id:int}/played-at")]
        [SwaggerOperation(OperationId = "MarkResourceAsNotPlayed")]
        public async Task<BaseResponse> MarkAsNotPlayed(int id)
        {
            await service.MarkAsNotPlayed(id);
            return BaseResponseBuilder.Ok;
        }


        [HttpGet("search/keyword-recommendation")]
        [SwaggerOperation(OperationId = "GetResourceSearchKeywordRecommendation")]
        public async Task<ListResponse<string>> GetResourceSearchKeywordRecommendation(string keyword,
            int maxCount = 10)
        {
            var keywords = new List<string>();
            var mediaLibraries = await mediaLibraryV2Service.GetAll(x => x.Name.Contains(keyword));
            keywords.AddRange(mediaLibraries.Select(ml => ml.Name).Distinct());

            var set = keywords.ToHashSet();

            if (keywords.Count < maxCount)
            {
                var pvs = await customPropertyValueService.GetAll(null, CustomPropertyValueAdditionalItem.BizValue,
                    false);
                var propertyValuesGroups = pvs.GroupBy(d => d.PropertyId).ToDictionary(d => d.Key, d => d.ToArray());
                foreach (var (pId, values) in propertyValuesGroups)
                {
                    var p = values[0].Property!.ToProperty();
                    var psh = PropertySystem.Property.GetSearchHandler(p.Type);
                    var filter = psh.BuildSearchFilterByKeyword(p, keyword);
                    if (filter != null)
                    {
                        foreach (var pv in values)
                        {
                            if (psh.IsMatch(pv.Value, filter.Operation, filter.DbValue))
                            {
                                var pd = PropertySystem.Property.GetDescriptor(p.Type);
                                var bizValue = pd.GetBizValue(p, filter.DbValue);
                                var svh = StandardValueSystem.GetHandler(p.Type.GetBizValueType());
                                var kw = svh.BuildDisplayValue(pv.BizValue);
                                if (kw.IsNotEmpty() && set.Add(kw))
                                {
                                    keywords.Add(kw);
                                }

                                if (keywords.Count >= maxCount)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return new ListResponse<string>(keywords);
        }

        #region Multi-Library Support

        /// <summary>
        /// Get all media library mappings for a resource
        /// </summary>
        [HttpGet("{id:int}/media-libraries")]
        [SwaggerOperation(OperationId = "GetResourceMediaLibraries")]
        public async Task<ListResponse<MediaLibraryResourceMapping>> GetResourceMediaLibraries(int id)
        {
            var mappings = await mappingService.GetByResourceId(id);
            return new ListResponse<MediaLibraryResourceMapping>(mappings);
        }

        /// <summary>
        /// Add a media library mapping to a resource
        /// </summary>
        [HttpPost("{id:int}/media-libraries/{mediaLibraryId:int}")]
        [SwaggerOperation(OperationId = "AddResourceMediaLibraryMapping")]
        public async Task<BaseResponse> AddResourceMediaLibraryMapping(int id, int mediaLibraryId)
        {
            // Verify the resource exists
            var resource = await service.Get(id, ResourceAdditionalItem.None);
            if (resource == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            // Verify the media library exists
            var mediaLibrary = await mediaLibraryV2Service.Get(mediaLibraryId);
            if (mediaLibrary == null)
            {
                return BaseResponseBuilder.BuildBadRequest($"Media library {mediaLibraryId} not found");
            }

            await mappingService.EnsureMappings(id, new[] { mediaLibraryId });
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Remove a media library mapping from a resource
        /// </summary>
        [HttpDelete("{id:int}/media-libraries/{mediaLibraryId:int}")]
        [SwaggerOperation(OperationId = "RemoveResourceMediaLibraryMapping")]
        public async Task<BaseResponse> RemoveResourceMediaLibraryMapping(int id, int mediaLibraryId)
        {
            var mappings = await mappingService.GetByResourceId(id);
            var mapping = mappings.FirstOrDefault(m => m.MediaLibraryId == mediaLibraryId);
            if (mapping == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            await mappingService.Delete(mapping.Id);
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Replace all media library mappings for a resource
        /// </summary>
        [HttpPut("{id:int}/media-libraries")]
        [SwaggerOperation(OperationId = "ReplaceResourceMediaLibraryMappings")]
        public async Task<BaseResponse> ReplaceResourceMediaLibraryMappings(int id, [FromBody] ResourceMediaLibraryMappingInputModel model)
        {
            // Verify the resource exists
            var resource = await service.Get(id, ResourceAdditionalItem.None);
            if (resource == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            // Verify all media libraries exist
            var mediaLibraries = await mediaLibraryV2Service.GetByKeys(model.MediaLibraryIds.ToArray());
            var foundIds = mediaLibraries.Select(m => m.Id).ToHashSet();
            var notFoundIds = model.MediaLibraryIds.Where(id => !foundIds.Contains(id)).ToList();
            if (notFoundIds.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"Media libraries not found: {string.Join(", ", notFoundIds)}");
            }

            await mappingService.ReplaceMappings(id, model.MediaLibraryIds);
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Bulk add media library mappings to multiple resources
        /// </summary>
        [HttpPost("bulk/media-libraries")]
        [SwaggerOperation(OperationId = "BulkAddResourceMediaLibraryMappings")]
        public async Task<BaseResponse> BulkAddMediaLibraryMappings([FromBody] BulkResourceMediaLibraryMappingInputModel model)
        {
            // Verify all resources exist
            var resources = await service.GetByKeys(model.ResourceIds.ToArray());
            if (resources.Count != model.ResourceIds.Count)
            {
                var foundIds = resources.Select(r => r.Id).ToHashSet();
                var notFoundIds = model.ResourceIds.Where(id => !foundIds.Contains(id)).ToList();
                return BaseResponseBuilder.BuildBadRequest($"Resources not found: {string.Join(", ", notFoundIds)}");
            }

            // Verify all media libraries exist
            var mediaLibraries = await mediaLibraryV2Service.GetByKeys(model.MediaLibraryIds.ToArray());
            var foundMlIds = mediaLibraries.Select(m => m.Id).ToHashSet();
            var notFoundMlIds = model.MediaLibraryIds.Where(id => !foundMlIds.Contains(id)).ToList();
            if (notFoundMlIds.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"Media libraries not found: {string.Join(", ", notFoundMlIds)}");
            }

            foreach (var resourceId in model.ResourceIds)
            {
                await mappingService.EnsureMappings(resourceId, model.MediaLibraryIds);
            }

            return BaseResponseBuilder.Ok;
        }

        #endregion
    }
}

/// <summary>
/// Input model for bulk resource media library mapping
/// </summary>
public class BulkResourceMediaLibraryMappingInputModel
{
    /// <summary>
    /// Resource IDs to add mappings to
    /// </summary>
    public List<int> ResourceIds { get; set; } = new();

    /// <summary>
    /// Media library IDs to associate with the resources
    /// </summary>
    public List<int> MediaLibraryIds { get; set; } = new();
}