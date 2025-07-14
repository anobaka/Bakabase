using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
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
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.Search.Models.Db;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        ICategoryService categoryService,
        ICoverDiscoverer coverDiscoverer,
        IPropertyService propertyService,
        ICustomPropertyService customPropertyService,
        IPropertyLocalizer propertyLocalizer,
        IMediaLibraryService mediaLibraryService,
        IMediaLibraryV2Service mediaLibraryV2Service,
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
                pt = PropertyInternals.BuiltinPropertyMap.GetValueOrDefault((ResourceProperty) propertyId)?.Type;
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

            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(pt.Value);

            return new ListResponse<SearchOperation>(psh?.SearchOperations.Keys);
        }

        [HttpGet("filter-value-property")]
        [SwaggerOperation(OperationId = "GetFilterValueProperty")]
        public async Task<SingletonResponse<PropertyViewModel>> GetFilterValueProperty(PropertyPool propertyPool,
            int propertyId,
            SearchOperation operation)
        {
            var p = await propertyService.GetProperty(propertyPool, propertyId);

            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(p.Type);
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
            var viewModels = await arr.ToViewModels(propertyService, propertyLocalizer);
            return new SingletonResponse<ResourceSearchViewModel?>(viewModels[0]);
        }

        [HttpPost("saved-search")]
        [SwaggerOperation(OperationId = "SaveNewResourceSearch")]
        public async Task<BaseResponse> SaveNewSearch([FromBody] SavedSearchAddInputModel model)
        {
            model.Search.StandardPageable();
            await resourceOptionsManager.SaveAsync(x =>
            {
                x.SavedSearches.Add(new ResourceOptions.SavedSearch
                    {Search = model.Search.ToDbModel(), Name = model.Name});
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("saved-search/{idx:int}/name")]
        [SwaggerOperation(OperationId = "PutSavedSearchName")]
        public async Task<BaseResponse> PutSavedSearchName(int idx, [FromBody] string name)
        {
            var searches = resourceOptionsManager.Value.SavedSearches ?? [];
            if (searches.Count <= idx)
            {
                return BaseResponseBuilder.BuildBadRequest($"Invalid {nameof(idx)}:{idx}");
            }

            searches[idx].Name = name;
            await resourceOptionsManager.SaveAsync(x => { x.SavedSearches = searches; });
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("saved-search")]
        [SwaggerOperation(OperationId = "GetSavedSearches")]
        public async Task<ListResponse<SavedSearchViewModel>> GetSavedSearches()
        {
            var searches = resourceOptionsManager.Value.SavedSearches;
            var dbModels = searches.Select(x => x.Search).ToArray();
            var viewModels = await dbModels.ToViewModels(propertyService, propertyLocalizer);
            var ret = searches.Select((s, i) => new SavedSearchViewModel(viewModels[i], s.Name));
            return new ListResponse<SavedSearchViewModel>(ret);
        }

        [HttpDelete("saved-search/{idx:int}")]
        [SwaggerOperation(OperationId = "DeleteSavedSearch")]
        public async Task<BaseResponse> DeleteSavedSearch(int idx)
        {
            await resourceOptionsManager.SaveAsync(x =>
            {
                if (idx < x.SavedSearches.Count)
                {
                    x.SavedSearches.RemoveAt(idx);
                }
            });
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("search")]
        [SwaggerOperation(OperationId = "SearchResources")]
        public async Task<SearchResponse<Resource>> Search([FromBody] ResourceSearchInputModel model, bool saveSearch)
        {
            model.StandardPageable();

            if (saveSearch)
            {
                try
                {
                    await resourceOptionsManager.SaveAsync(a => a.LastSearchV2 = model.ToDbModel());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to save search criteria");
                }
            }

            var domainModel = await model.ToDomainModel(propertyService);

            return await service.Search(domainModel);
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
            return new ListResponse<string>(await service.GetPlayableFiles(id, HttpContext.RequestAborted) ??
                                            new string[] { });
        }

        [HttpPut("move")]
        [SwaggerOperation(OperationId = "MoveResources")]
        public async Task<BaseResponse> Move([FromBody] ResourceMoveRequestModel model)
        {
            var resources = (await service.GetByKeys(model.Ids)).ToDictionary(t => t.Id, t => t);
            if (!resources.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"Resources [{string.Join(',', model.Ids)}] are not found");
            }

            string? mediaLibraryName;
            if (model.IsLegacyMediaLibrary)
            {
                var mediaLibrary = await mediaLibraryService.Get(model.MediaLibraryId, MediaLibraryAdditionalItem.Category);
                mediaLibraryName = mediaLibrary?.Name;
                if (mediaLibrary?.Category != null)
                {
                    mediaLibraryName = $"[{mediaLibrary.Category.Name}]{mediaLibrary.Name}";
                }
            }
            else
            {
                var mediaLibrary = await mediaLibraryV2Service.Get(model.MediaLibraryId);
                mediaLibraryName = mediaLibrary?.Name;
            }

            if (mediaLibraryName.IsNullOrEmpty())
            {
                return BaseResponseBuilder.BuildBadRequest($"Invalid {nameof(model.MediaLibraryId)}");
            }

            if (model.Path.IsNullOrEmpty())
            {
                await resourceOptionsManager.SaveAsync(x => x.AddIdOfMediaLibraryRecentlyMovedTo(model.MediaLibraryId));
                await service.ChangeMediaLibrary(model.Ids, model.MediaLibraryId, model.IsLegacyMediaLibrary);
                return BaseResponseBuilder.Ok;
            }

            await fsOptionsManager.SaveAsync(x => x.AddRecentMovingDestination(model.Path.StandardizePath()!));

            var taskName = $"Resource:BulkMove:{CryptographyUtils.Md5(string.Join(',', resources.Keys))}";

            var rand = new Random();
            foreach (var (id, resource) in resources)
            {
                var targetPath = Path.Combine(model.Path, resource.FileName).StandardizePath()!;

                await taskManager.Enqueue(new BTaskHandlerBuilder
                {
                    ConflictKeys = [taskName],
                    ResourceType = BTaskResourceType.Resource,
                    GetName = localizer.MoveResource,
                    GetDescription = () => localizer.MoveResourceDetail(resource.Path, mediaLibraryName, targetPath),
                    GetMessageOnInterruption = localizer.MessageOnInterruption_MoveFiles,
                    Type = BTaskType.MoveResources,
                    ResourceKeys = [id],
                    Run = async args =>
                    {
                        // var fakeDelay = rand.Next(50, 300);
                        // for (var i = 0; i < 100; i++)
                        // {
                        //     await args.UpdateTask(t => t.Percentage = i + 1);
                        //     await Task.Delay(fakeDelay, args.CancellationToken);
                        // }

                        if (resource.IsFile)
                        {
                            await FileUtils.MoveAsync(resource.Path, targetPath, false,
                                async p => await args.UpdateTask(t => t.Percentage = p),
                                PauseToken.None,
                                args.CancellationToken);
                        }
                        else
                        {
                            await DirectoryUtils.MoveAsync(resource.Path, targetPath, false,
                                async p => await args.UpdateTask(t => t.Percentage = p),
                                PauseToken.None,
                                args.CancellationToken);
                        }

                        if (resource.MediaLibraryId != model.MediaLibraryId)
                        {
                            await using var scope = args.RootServiceProvider.CreateAsyncScope();
                            var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();
                            await resourceService.ChangeMediaLibrary([resource.Id], model.MediaLibraryId,
                                model.IsLegacyMediaLibrary,
                                new Dictionary<int, string> {{resource.Id, targetPath}});
                        }
                    }
                });
            }

            return BaseResponseBuilder.Ok;
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

        [HttpGet("{resourceId}/play")]
        [SwaggerOperation(OperationId = "PlayResourceFile")]
        public async Task<BaseResponse> Play(int resourceId, string file)
        {
            return await service.Play(resourceId, file);
        }

        [HttpDelete("ids")]
        [SwaggerOperation(OperationId = "DeleteResourcesByKeys")]
        public async Task<BaseResponse> DeleteByKeys(int[] ids, bool deleteFiles)
        {
            await service.DeleteByKeys(ids, deleteFiles);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("unknown")]
        [SwaggerOperation(OperationId = "GetUnknownResources")]
        public async Task<ListResponse<Resource>> GetUnknownResources()
        {
            var resources = await service.GetUnknownResources();
            return new ListResponse<Resource>(resources);
        }

        [HttpGet("unknown/count")]
        [SwaggerOperation(OperationId = "GetUnknownResourcesCount")]
        public async Task<SingletonResponse<int>> GetUnknownCount()
        {
            return new SingletonResponse<int>(data: await service.GetUnknownCount());
        }

        [HttpDelete("unknown")]
        [SwaggerOperation(OperationId = "DeleteUnknownResources")]
        public async Task<BaseResponse> DeleteUnknown()
        {
            await service.DeleteUnknown();
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
    }
}