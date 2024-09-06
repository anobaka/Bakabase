﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
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
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Components.Properties.Tags;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;
using Image = SixLabors.ImageSharp.Image;

namespace Bakabase.Service.Controllers
{
    [Route("~/resource")]
    public class ResourceController : Controller
    {
        private readonly IResourceService _service;
        private readonly ISpecialTextService _specialTextService;
        private readonly IMediaLibraryService _mediaLibraryService;
        private readonly ResourceTaskManager _resourceTaskManager;
        private readonly BackgroundTaskManager _taskManager;
        private readonly IWebHostEnvironment _env;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
        private readonly InsideWorldLocalizer _localizer;
        private readonly FfMpegService _ffMpegService;
        private readonly IBOptions<ResourceOptions> _resourceOptions;
        private readonly InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.FfMpegService _ffMpegInstaller;
        private readonly ILogger<ResourceController> _logger;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly ICategoryService _categoryService;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICoverDiscoverer _coverDiscoverer;
        private readonly IStandardValueHelper _standardValueHelper;

        public ResourceController(IResourceService service, IServiceProvider serviceProvider,
            ISpecialTextService specialTextService, IMediaLibraryService mediaLibraryService,
            ResourceTaskManager resourceTaskManager, BackgroundTaskManager taskManager, IWebHostEnvironment env,
            InsideWorldOptionsManagerPool insideWorldOptionsManager, InsideWorldLocalizer localizer,
            FfMpegService ffMpegService, IBOptions<ResourceOptions> resourceOptions,
            InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.FfMpegService ffMpegInstaller,
            ILogger<ResourceController> logger, ICustomPropertyValueService customPropertyValueService,
            ICategoryService categoryService, ICustomPropertyService customPropertyService,
            ICoverDiscoverer coverDiscoverer, IStandardValueHelper standardValueHelper)
        {
            _service = service;
            _specialTextService = specialTextService;
            _mediaLibraryService = mediaLibraryService;
            _resourceTaskManager = resourceTaskManager;
            _taskManager = taskManager;
            _env = env;
            _insideWorldOptionsManager = insideWorldOptionsManager;
            _localizer = localizer;
            _ffMpegService = ffMpegService;
            _resourceOptions = resourceOptions;
            _ffMpegInstaller = ffMpegInstaller;
            _logger = logger;
            _customPropertyValueService = customPropertyValueService;
            _categoryService = categoryService;
            _customPropertyService = customPropertyService;
            _coverDiscoverer = coverDiscoverer;
            _standardValueHelper = standardValueHelper;
        }

        [HttpGet("search-criteria")]
        [SwaggerOperation(OperationId = "GetResourceSearchCriteria")]
        public async Task<SingletonResponse<ResourceSearchDto?>> GetSearchCriteria()
        {
            var sc = _resourceOptions.Value.LastSearchV2;
            if (sc != null)
            {
                if (sc.Group != null)
                {
                    var filters = sc.Group.ExtractFilters();
                    var customPropertyIds =
                        filters.Where(d => d.IsCustomProperty).Select(d => d.PropertyId).ToHashSet();
                    var customPropertyMap =
                        (await _customPropertyService.GetByKeys(customPropertyIds, CustomPropertyAdditionalItem.None))
                        .ToDictionary(d => d.Id, d => d);

                    Dictionary<int, MediaLibrary>? mediaLibraryMap = null;
                    Dictionary<int, Category>? categoryMap = null;
                    var propertyMap = (await _customPropertyService.GetAll()).ToDictionary(d => d.Id, d => d);

                    foreach (var filter in filters)
                    {
                        if (string.IsNullOrEmpty(filter.DbValue))
                        {
                            continue;
                        }

                        if (filter.IsCustomProperty)
                        {
                            var property = customPropertyMap.GetValueOrDefault(filter.PropertyId);
                            if (property == null)
                            {
                                continue;
                            }

                            switch (property.EnumType)
                            {
                                case CustomPropertyType.SingleLineText:
                                case CustomPropertyType.MultilineText:
                                case CustomPropertyType.Date:
                                case CustomPropertyType.DateTime:
                                case CustomPropertyType.Time:
                                case CustomPropertyType.Formula:
                                case CustomPropertyType.Number:
                                case CustomPropertyType.Percentage:
                                case CustomPropertyType.Rating:
                                case CustomPropertyType.Boolean:
                                case CustomPropertyType.Attachment:
                                case CustomPropertyType.Link:
                                {
                                    filter.BizValue = filter.DbValue;
                                    break;
                                }
                                case CustomPropertyType.SingleChoice:
                                {
                                    switch (filter.Operation)
                                    {
                                        case SearchOperation.Equals:
                                        case SearchOperation.NotEquals:
                                        {
                                            var value = _standardValueHelper.Deserialize<string>(filter.DbValue,
                                                StandardValueType.String);
                                            if (!string.IsNullOrEmpty(value))
                                            {
                                                var choices = (propertyMap.GetValueOrDefault(filter.PropertyId) as
                                                    SingleChoiceProperty)?.Options?.Choices;
                                                if (choices?.Any() == true)
                                                {
                                                    filter.BizValue = choices.FirstOrDefault(x => x.Value == value)
                                                        ?.Label;
                                                }
                                            }

                                            break;
                                        }
                                        case SearchOperation.In:
                                        case SearchOperation.NotIn:
                                        {
                                            var value = _standardValueHelper
                                                .Deserialize<List<string>>(filter.DbValue, StandardValueType.ListString)
                                                ?.OfType<string>().ToList();
                                            if (value?.Any() == true)
                                            {
                                                var choices = (propertyMap.GetValueOrDefault(filter.PropertyId) as
                                                    SingleChoiceProperty)?.Options?.Choices;
                                                if (choices?.Any() == true)
                                                {
                                                    filter.BizValue = _standardValueHelper.Serialize(value
                                                        .Select(v => choices.FirstOrDefault(x => x.Value == v)?.Label)
                                                        .OfType<string>().ToList(), StandardValueType.ListString);
                                                }

                                            }

                                            break;
                                        }
                                    }

                                    break;
                                }
                                case CustomPropertyType.MultipleChoice:
                                {
                                    switch (filter.Operation)
                                    {
                                        case SearchOperation.Contains:
                                        case SearchOperation.NotContains:
                                        {
                                            var value = _standardValueHelper
                                                .Deserialize<List<string>>(filter.DbValue, StandardValueType.ListString)
                                                ?.OfType<string>()
                                                .ToList();
                                            if (value?.Any() == true)
                                            {
                                                var choices =
                                                    (propertyMap.GetValueOrDefault(filter.PropertyId) as
                                                        MultipleChoiceProperty)?.Options?.Choices;
                                                if (choices?.Any() == true)
                                                {
                                                    filter.BizValue = _standardValueHelper.Serialize(value
                                                        .Select(v => choices.FirstOrDefault(x => x.Value == v)?.Label)
                                                        .OfType<string>().ToList(), StandardValueType.ListString);
                                                }

                                            }

                                            break;
                                        }
                                    }

                                    break;
                                }
                                case CustomPropertyType.Multilevel:
                                {
                                    switch (filter.Operation)
                                    {
                                        case SearchOperation.Contains:
                                        case SearchOperation.NotContains:
                                        {
                                            var value = _standardValueHelper
                                                .Deserialize<List<string>>(filter.DbValue, StandardValueType.ListString)
                                                ?.OfType<string>()
                                                .ToList();
                                            if (value?.Any() == true)
                                            {
                                                var trees = (propertyMap.GetValueOrDefault(filter.PropertyId) as
                                                    MultilevelProperty)?.Options?.Data;
                                                if (trees?.Any() == true)
                                                {
                                                    filter.BizValue = _standardValueHelper.Serialize(value
                                                        .Select(v => trees.FindLabelChain(v)?.ToList())
                                                        .OfType<List<string>>().ToList(), StandardValueType.ListListString);
                                                }
                                            }

                                            break;
                                        }
                                    }

                                    break;
                                }
                                case CustomPropertyType.Tags:
                                {
                                    switch (filter.Operation)
                                    {
                                        case SearchOperation.Contains:
                                        case SearchOperation.NotContains:
                                        {
                                            var value = _standardValueHelper
                                                .Deserialize<List<string>>(filter.DbValue, StandardValueType.ListString)
                                                ?.OfType<string>()
                                                .ToList();
                                            if (value?.Any() == true)
                                            {
                                                var tags =
                                                    (propertyMap.GetValueOrDefault(filter.PropertyId) as TagsProperty)
                                                    ?.Options?.Tags;
                                                if (tags?.Any() == true)
                                                {
                                                    filter.BizValue = _standardValueHelper.Serialize(value
                                                        .Select(v =>
                                                        {
                                                            var tag = tags.FirstOrDefault(x => x.Value == v);
                                                            if (tag == null)
                                                            {
                                                                return null;
                                                            }

                                                            return new TagValue(tag.Group, tag.Name);
                                                        })
                                                        .OfType<TagValue>()
                                                        .ToList(), StandardValueType.ListTag);
                                                }
                                            }

                                            break;
                                        }
                                    }

                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                        else
                        {
                            var rp = (SearchableReservedProperty) filter.PropertyId;
                            var valueTypes =
                                InternalOptions.SearchableResourcePropertyAndValueTypesMap.GetValueOrDefault(rp);
                            if (valueTypes == null)
                            {
                                switch (rp)
                                {
                                    case SearchableReservedProperty.MediaLibrary:
                                    {
                                        if (!string.IsNullOrEmpty(filter.DbValue))
                                        {
                                            mediaLibraryMap ??= (await _mediaLibraryService.GetAll(null,
                                                MediaLibraryAdditionalItem.None)).ToDictionary(d => d.Id, d => d);

                                            categoryMap ??=
                                                (await _categoryService.GetAll(null, CategoryAdditionalItem.None))
                                                .ToDictionary(d => d.Id, d => d);

                                            var ids = _standardValueHelper.Deserialize<List<string>>(filter.DbValue,
                                                StandardValueType
                                                    .ListString)?.Select(int.Parse) ?? [];

                                            var tMlMap = mediaLibraryMap;
                                            var tCMap = categoryMap;
                                            var bizValue = new List<List<string>>();
                                            foreach (var id in ids)
                                            {
                                                var ml = tMlMap.GetValueOrDefault(id);
                                                if (ml == null)
                                                {
                                                    continue;
                                                }

                                                var c = tCMap.GetValueOrDefault(ml.CategoryId);
                                                if (c == null)
                                                {
                                                    continue;
                                                }

                                                bizValue.Add([c.Name, ml.Name]);
                                            }

                                            filter.BizValue = _standardValueHelper.Serialize(bizValue, StandardValueType.ListListString);
                                        }

                                        break;
                                    }
                                    case SearchableReservedProperty.FileName:
                                    case SearchableReservedProperty.DirectoryPath:
                                    case SearchableReservedProperty.CreatedAt:
                                    case SearchableReservedProperty.FileCreatedAt:
                                    case SearchableReservedProperty.FileModifiedAt:
                                    case SearchableReservedProperty.Introduction:
                                    case SearchableReservedProperty.Rating:
                                    {
                                        filter.BizValue = filter.DbValue;
                                        break;
                                    }
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                        }
                    }
                }
            }

            return new SingletonResponse<ResourceSearchDto>(sc);
        }

        [HttpPost("search")]
        [SwaggerOperation(OperationId = "SearchResources")]
        public async Task<SearchResponse<Resource>> Search([FromBody] ResourceSearchInputModel model)
        {
            return await _service.Search(model, model.SaveSearchCriteria, false);
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

            if (!string.IsNullOrEmpty(cover.Path))
            {
                return File(System.IO.File.OpenRead(cover.Path), MimeTypes.GetMimeType(cover.Path));
            }

            var format = await Image.DetectFormatAsync(new MemoryStream(cover.Data!));
            return File(cover.Data!, format.DefaultMimeType);
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