using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Abstractions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;
using Image = SixLabors.ImageSharp.Image;

namespace Bakabase.Service.Controllers
{
    [Route("~/resource")]
    public class ResourceController : Controller
    {
        private readonly IResourceService _service;
        private readonly ResourceTaskManager _resourceTaskManager;
        private readonly FfMpegService _ffMpegService;
        private readonly IBOptions<ResourceOptions> _resourceOptions;
        private readonly FfMpegService _ffMpegInstaller;
        private readonly ILogger<ResourceController> _logger;
        private readonly ICategoryService _categoryService;
        private readonly ICoverDiscoverer _coverDiscoverer;
        private readonly IPropertyService _propertyService;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly IPropertyLocalizer _propertyLocalizer;
        public ResourceController(IResourceService service, ResourceTaskManager resourceTaskManager,
            FfMpegService ffMpegService, IBOptions<ResourceOptions> resourceOptions, FfMpegService ffMpegInstaller,
            ILogger<ResourceController> logger, ICategoryService categoryService, ICoverDiscoverer coverDiscoverer,
            IPropertyService propertyService, ICustomPropertyService customPropertyService, IPropertyLocalizer propertyLocalizer)
        {
            _service = service;
            _resourceTaskManager = resourceTaskManager;
            _ffMpegService = ffMpegService;
            _resourceOptions = resourceOptions;
            _ffMpegInstaller = ffMpegInstaller;
            _logger = logger;
            _categoryService = categoryService;
            _coverDiscoverer = coverDiscoverer;
            _propertyService = propertyService;
            _customPropertyService = customPropertyService;
            _propertyLocalizer = propertyLocalizer;
        }

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
                var p = await _customPropertyService.GetByKey(propertyId);
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
            var p = await _propertyService.GetProperty(propertyPool, propertyId);

            var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(p.Type);
            var options = psh?.SearchOperations.GetValueOrDefault(operation);
            if (options == null)
            {
                return SingletonResponseBuilder<PropertyViewModel>.NotFound;
            }

            if (options.ConvertProperty != null)
            {
                p = options.ConvertProperty(p);
            }

            return new SingletonResponse<PropertyViewModel>(p.ToViewModel(_propertyLocalizer));
        }

        [HttpGet("search-criteria")]
        [SwaggerOperation(OperationId = "GetResourceSearchCriteria")]
        public async Task<SingletonResponse<ResourceSearchViewModel?>> GetSearchCriteria()
        {
            var sc = _resourceOptions.Value.LastSearchV2?.ToDomainModel();
            if (sc?.Group != null)
            {
                var filters = sc.Group.ExtractFilters();
                if (filters.Any())
                {
                    var pool = filters.Aggregate((PropertyPool) 0, (a, b) => a | b.PropertyPool);
                    var propertyMap = (await _propertyService.GetProperties(pool)).GroupBy(d => d.Pool)
                        .ToDictionary(d => d.Key, d => d.ToDictionary(x => x.Id));
                    foreach (var filter in filters)
                    {
                        var property = propertyMap.GetValueOrDefault(filter.PropertyPool)
                            ?.GetValueOrDefault(filter.PropertyId);
                        if (property != null)
                        {
                            filter.Property = property;
                        }
                    }
                }
            }

            return new SingletonResponse<ResourceSearchViewModel?>(sc?.ToViewModel(_propertyLocalizer));
        }

        [HttpPost("search")]
        [SwaggerOperation(OperationId = "SearchResources")]
        public async Task<SearchResponse<Resource>> Search([FromBody] ResourceSearchInputModel model)
        {
            return await _service.Search(model.ToDomainModel(), model.SaveSearchCriteria, false);
        }

        [HttpGet("keys")]
        [SwaggerOperation(OperationId = "GetResourcesByKeys")]
        public async Task<ListResponse<Resource>> GetByKeys([FromQuery] int[] ids,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            return new ListResponse<Resource>(await _service.GetByKeys(ids, additionalItems));
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
            var resource = await _service.Get(id, ResourceAdditionalItem.None);
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
            var resource = await _service.Get(id, ResourceAdditionalItem.None);
            if (resource == null)
            {
                return NotFound();
            }

            var category = await _categoryService.Get(resource.CategoryId, CategoryAdditionalItem.None);

            var cover = await _coverDiscoverer.Discover(resource.Path,
                category?.CoverSelectionOrder ?? CoverSelectOrder.FilenameAscending, true, HttpContext.RequestAborted);
            if (cover == null)
            {
                return NotFound();
            }

            if (cover.Data != null)
            {
                var format = await Image.DetectFormatAsync(new MemoryStream(cover.Data!));
                return File(cover.Data!, format.DefaultMimeType);
            }

            return File(System.IO.File.OpenRead(cover.Path), MimeTypes.GetMimeType(cover.Path));
        }

        [HttpGet("{id}/playable-files")]
        [SwaggerOperation(OperationId = "GetResourcePlayableFiles")]
        [ResponseCache(Duration = 20 * 60)]
        public async Task<ListResponse<string>> GetPlayableFiles(int id)
        {
            return new ListResponse<string>(await _service.GetPlayableFiles(id, HttpContext.RequestAborted) ??
                                            new string[] { });
        }

        // [HttpPut("move")]
        // [SwaggerOperation(OperationId = "MoveResources")]
        // public async Task<BaseResponse> Move([FromBody] ResourceMoveRequestModel model)
        // {
        // 	var resources = (await _service.GetByKeys(model.Ids)).ToDictionary(t => t.Id, t => t);
        // 	if (!resources.Any())
        // 	{
        // 		return BaseResponseBuilder.BuildBadRequest($"Resources [{string.Join(',', model.Ids)}] are not found");
        // 	}
        //
        // 	var mediaLibrary = model.MediaLibraryId.HasValue
        // 		? await _mediaLibraryService.GetByKey(model.MediaLibraryId.Value)
        // 		: null;
        //
        // 	var taskName = $"Resource:BulkMove:{DateTime.Now:HH:mm:ss}";
        // 	_taskManager.RunInBackground(taskName, new CancellationTokenSource(), async (bt, sp) =>
        // 		{
        // 			var resourceService = sp.GetRequiredService<ResourceService>();
        // 			bt.Message = _localizer.Resource_MovingTaskSummary(
        // 				resources.Select(r => r.Value.FileName).ToArray(), mediaLibrary?.Name, model.Path);
        // 			foreach (var id in model.Ids)
        // 			{
        // 				await _resourceTaskManager.Add(new ResourceTaskInfo
        // 				{
        // 					BackgroundTaskId = bt.Id,
        // 					Id = id,
        // 					Type = ResourceTaskType.Moving,
        // 					Summary = _localizer.Resource_MovingTaskSummary(null, mediaLibrary?.Name, model.Path),
        // 					OperationOnComplete = ResourceTaskOperationOnComplete.RemoveOnResourceView
        // 				});
        // 			}
        //
        // 			foreach (var (id, resource) in resources)
        // 			{
        // 				if (resource.IsFile)
        // 				{
        // 					await FileUtils.MoveAsync(resource.Path, model.Path, false,
        // 						async p => { await _resourceTaskManager.Update(id, t => t.Percentage = p); },
        // 						bt.Cts.Token);
        // 				}
        // 				else
        // 				{
        // 					await DirectoryUtils.MoveAsync(resource.Path, model.Path, false,
        // 						async p => { await _resourceTaskManager.Update(id, t => t.Percentage = p); },
        // 						bt.Cts.Token);
        // 				}
        //
        // 				// var p = 0;
        // 				// while (p < 100)
        // 				// {
        // 				//     p++;
        // 				//     await Task.Delay(100, bt.Cts.Token);
        // 				//     await _resourceTaskManager.Update(id, t => t.Percentage = p);
        // 				// }
        //
        // 				if (mediaLibrary != null && resource.MediaLibraryId != mediaLibrary.Id)
        // 				{
        // 					// todo: simpler way to change base info of resources.
        // 					var resourceDto = (await resourceService.GetByKey(id, ResourceAdditionalItem.All, true))!;
        // 					resourceDto.MediaLibraryId = mediaLibrary.Id;
        // 					resourceDto.CategoryId = mediaLibrary.CategoryId;
        // 					await resourceService.AddOrPatchRange(new List<Resource> {resourceDto});
        // 				}
        //
        // 				await _resourceTaskManager.Clear(id);
        // 			}
        //
        // 			return BaseResponseBuilder.Ok;
        // 		}, BackgroundTaskLevel.Default, null, null,
        // 		async task => await _resourceTaskManager.Update(resources.Keys.ToArray(), t => t.Error = task.Message));
        //
        // 	// await DirectoryUtils.Move(model.EntryPaths.ToDictionary(t => t, t => Path.Combine(model.DestDir, Path.GetFileName(t))), false, )
        //
        // 	return BaseResponseBuilder.Ok;
        // }

        [HttpDelete("{id}/task")]
        [SwaggerOperation(OperationId = "ClearResourceTask")]
        public async Task<BaseResponse> ClearTask(int id)
        {
            await _resourceTaskManager.Clear(id);
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
            var resource = await _service.Get(id, ResourceAdditionalItem.None);

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
            var ffmpegIsReady = _ffMpegInstaller.Status == DependentComponentStatus.Installed;

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
                                    (await _ffMpegService.GetDuration(f, HttpContext.RequestAborted))),
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
            return await _service.PutPropertyValue(id, model);
        }

        [HttpGet("{resourceId}/play")]
        [SwaggerOperation(OperationId = "PlayResourceFile")]
        public async Task<BaseResponse> Play(int resourceId, string file)
        {
            return await _service.Play(resourceId, file);
        }

        [HttpDelete("unknown")]
        [SwaggerOperation(OperationId = "DeleteUnknownResources")]
        public async Task<BaseResponse> DeleteUnknown()
        {
            await _service.DeleteUnknown();
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("unknown/count")]
        [SwaggerOperation(OperationId = "GetUnknownResourcesCount")]
        public async Task<SingletonResponse<int>> GetUnknownCount()
        {
            return new SingletonResponse<int>(data: await _service.GetUnknownCount());
        }
    }
}