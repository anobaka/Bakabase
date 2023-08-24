using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using ElectronNET.API.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Internal;
using SharpCompress.IO;
using SixLabors.ImageSharp.Processing;
using Swashbuckle.AspNetCore.Annotations;
using Xabe.FFmpeg;
using Image = SixLabors.ImageSharp.Image;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/resource")]
    public class ResourceController : Controller
    {
        private readonly ResourceService _service;
        private readonly ResourceTagMappingService _resourceTagMappingService;
        private readonly IServiceProvider _serviceProvider;
        private readonly CustomResourcePropertyService _customResourcePropertyService;
        private readonly SpecialTextService _specialTextService;
        private readonly MediaLibraryService _mediaLibraryService;
        private readonly ResourceTaskManager _resourceTaskManager;
        private readonly BackgroundTaskManager _taskManager;
        private static readonly object PretreatmentCacheLock = new object();
        private static ConcurrentDictionary<string, string> _pretreatmentCache;
        private static string _pretreatmentCacheVersion;
        private readonly FavoritesResourceMappingService _favoritesResourceMappingService;
        private readonly IWebHostEnvironment _env;
        private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
        private readonly InsideWorldLocalizer _localizer;
        private readonly FFMpegHelper _ffMpegHelper;
        private readonly TempFileManager _tempFileManager;

        private static readonly MemoryCache CoverCache = new MemoryCache("ResourceCover");

        private static readonly CacheItemPolicy _coverCacheItemPolicy = new CacheItemPolicy
        {
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };

        public ResourceController(ResourceService service,
            ResourceTagMappingService resourceTagMappingService,
            IServiceProvider serviceProvider, CustomResourcePropertyService customResourcePropertyService,
            SpecialTextService specialTextService, MediaLibraryService mediaLibraryService,
            ResourceTaskManager resourceTaskManager, BackgroundTaskManager taskManager,
            FavoritesResourceMappingService favoritesResourceMappingService, IWebHostEnvironment env,
            InsideWorldOptionsManagerPool insideWorldOptionsManager, InsideWorldLocalizer localizer,
            FFMpegHelper ffMpegHelper, TempFileManager tempFileManager)
        {
            _service = service;
            _resourceTagMappingService = resourceTagMappingService;
            _serviceProvider = serviceProvider;
            _customResourcePropertyService = customResourcePropertyService;
            _specialTextService = specialTextService;
            _mediaLibraryService = mediaLibraryService;
            _resourceTaskManager = resourceTaskManager;
            _taskManager = taskManager;
            _favoritesResourceMappingService = favoritesResourceMappingService;
            _env = env;
            _insideWorldOptionsManager = insideWorldOptionsManager;
            _localizer = localizer;
            _ffMpegHelper = ffMpegHelper;
            _tempFileManager = tempFileManager;
        }

        [HttpPost("search")]
        [SwaggerOperation(OperationId = "SearchResources")]
        public async Task<SearchResponse<ResourceDto>> Search([FromBody] ResourceSearchDto model)
        {
            return await _service.Search(model, false);
        }

        [HttpGet("keys")]
        [SwaggerOperation(OperationId = "GetResourcesByKeys")]
        public async Task<ListResponse<ResourceDto>> GetByKeys([FromQuery] int[] ids)
        {
            return new ListResponse<ResourceDto>(await _service.GetByKeys(ids));
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "PatchResource")]
        public async Task<BaseResponse> Update(int id, [FromBody] ResourceUpdateRequestModel model)
        {
            return await _service.Patch(id, model);
        }

        [HttpPut("tag")]
        [SwaggerOperation(OperationId = "UpdateResourceTags")]
        public async Task<BaseResponse> UpdateTags([FromBody] ResourceTagUpdateRequestModel model)
        {
            await _service.BatchUpdateTags(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("directory")]
        [SwaggerOperation(OperationId = "OpenResourceDirectory")]
        public async Task<BaseResponse> Open(int id)
        {
            var resource = await _service.GetByKey(id, ResourceAdditionalItem.None, false);
            var rawFileOrDirectoryName = Path.Combine(resource.Directory, resource.RawName);
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

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveResource")]
        public async Task<BaseResponse> Remove(int id)
        {
            await _service.RemoveByKey(id, true);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/raw-name")]
        [SwaggerOperation(OperationId = "UpdateResourceRawName")]
        public async Task<BaseResponse> UpdateRawName(int id, [FromBody] ResourceRawNameUpdateRequestModel model)
        {
            return await _service.UpdateRawName(id, model.RawName);
        }

        [HttpGet("{id}/cover")]
        [SwaggerOperation(OperationId = "GetResourceCover")]
        public async Task<IActionResult> GetCover(int id)
        {
            var cacheItem = CoverCache.Get(id.ToString());
            if (cacheItem is byte[] byteData)
            {
                return File(byteData, MimeTypes.GetMimeType(".png"));
            }

            var r = await _service.DiscoverAndPopulateCoverStream(id, HttpContext.RequestAborted);
            if (r.HasValue)
            {
                var (ext, stream) = r.Value;
                if (stream is not MemoryStream ms)
                {
                    ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                }

                var data = ms.ToArray();
                CoverCache.Set(id.ToString(), data, _coverCacheItemPolicy);
                return File(data, MimeTypes.GetMimeType(ext));
            }

            return NotFound();
        }

        [HttpDelete("{id}/cover/cache")]
        [SwaggerOperation(OperationId = "RemoveCoverCache")]
        public Task<BaseResponse> RemoveCoverCache(int id)
        {
            CoverCache.Remove(id.ToString());
            return Task.FromResult(BaseResponseBuilder.Ok);
        }

        [HttpGet("{id}/playable-files")]
        [SwaggerOperation(OperationId = "GetResourcePlayableFiles")]
        public async Task<ListResponse<string>> GetPlayableFiles(int id)
        {
            return new ListResponse<string>(await _service.GetPlayableFiles(id, HttpContext.RequestAborted) ??
                                            new string[] { });
        }

        [HttpGet("custom-properties-and-candidates")]
        [SwaggerOperation(OperationId = "GetAllCustomPropertiesAndCandidates")]
        public async Task<SingletonResponse<Dictionary<string, string[]>>> GetAllCustomPropertiesAndCandidates()
        {
            var data = await _customResourcePropertyService.GetAll();
            var result = data.GroupBy(t => t.Key).ToDictionary(t => t.Key,
                t => t.Select(b => b.Value).Where(b => b.IsNotEmpty()).ToHashSet().ToArray());
            return new(result);
        }

        [HttpGet("reserved-properties-and-candidates")]
        [SwaggerOperation(OperationId = "GetAllReservedPropertiesAndCandidates")]
        public async Task<SingletonResponse<Dictionary<string, string[]>>> GetAllReservedPropertiesAndCandidates()
        {
            var data = await _service.GetAllReservedPropertiesAndCandidates();
            return new SingletonResponse<Dictionary<string, string[]>>(data);
        }


        [HttpDelete("resource/{id}/custom-property/{propertyKey}")]
        [SwaggerOperation(OperationId = "RemoveResourceCustomProperty")]
        public async Task<BaseResponse> DeleteCustomProperty(int id, string propertyKey)
        {
            return await _customResourcePropertyService.RemoveAll(t => t.ResourceId == id && t.Key == propertyKey);
        }

        [HttpGet("existence")]
        [SwaggerOperation(OperationId = "CheckResourceExistence")]
        public async Task<SingletonResponse<ResourceExistenceResult>> CheckExistence(string name)
        {
            lock (PretreatmentCacheLock)
            {
                if (_pretreatmentCacheVersion != SpecialTextService.Version)
                {
                    _pretreatmentCacheVersion = SpecialTextService.Version;
                    _pretreatmentCache = new();
                }
            }

            var cleanName = await _specialTextService.Pretreatment(name);
            var allResources = (await _service.GetAllEntities(null, false)).GroupBy(t => t.RawName)
                .ToDictionary(t => t.Key, t => t.ToList());
            var names = allResources.ToDictionary(t => t.Key, t => t.Value.Select(r => r.RawFullname).ToArray());
            var familiarNames = new List<(string RawFullname, decimal Diff)>();
            foreach (var n in names.Keys)
            {
                if (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return null;
                }

                if (!_pretreatmentCache.TryGetValue(n, out var cn))
                {
                    _pretreatmentCache[n] = cn = await _specialTextService.Pretreatment(n);
                }

                if (cn == cleanName)
                {
                    return new SingletonResponse<ResourceExistenceResult>(new ResourceExistenceResult
                    {
                        Existence = ResourceExistence.Exist,
                        Resources = names[n]
                    });
                }

                var maxLength = Math.Max(cleanName.Length, cn.Length);
                var lengthDis = (decimal) Math.Abs(cleanName.Length - cn.Length);
                var lengthDisRate = lengthDis / maxLength;
                const decimal familiarDiffThreshold = 0.2m;

                if (lengthDisRate <= familiarDiffThreshold)
                {
                    var dis = cn.GetLevenshteinDistance(cleanName);
                    var rate = (decimal) dis / maxLength;
                    if (rate <= familiarDiffThreshold)
                    {
                        familiarNames.AddRange(names[n].Select(t => (t, rate)));
                    }
                }
            }

            if (familiarNames.Any())
            {
                return new SingletonResponse<ResourceExistenceResult>(new ResourceExistenceResult
                {
                    Existence = ResourceExistence.Maybe,
                    Resources = familiarNames.OrderBy(t => t.Diff).Take(15).Select(t => t.RawFullname).ToArray()
                });
            }

            return new SingletonResponse<ResourceExistenceResult>(new ResourceExistenceResult
                {Existence = ResourceExistence.New});
        }

        [HttpPut("move")]
        [SwaggerOperation(OperationId = "MoveResources")]
        public async Task<BaseResponse> Move([FromBody] ResourceMoveRequestModel model)
        {
            var resources = (await _service.GetByKeys(model.Ids)).ToDictionary(t => t.Id, t => t);
            if (!resources.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"Resources [{string.Join(',', model.Ids)}] are not found");
            }

            var mediaLibrary = model.MediaLibraryId.HasValue
                ? await _mediaLibraryService.GetByKey(model.MediaLibraryId.Value)
                : null;

            var taskName = $"Resource:BulkMove:{DateTime.Now:HH:mm:ss}";
            _taskManager.RunInBackground(taskName, new CancellationTokenSource(), async (bt, sp) =>
                {
                    var resourceService = sp.GetRequiredService<ResourceService>();
                    bt.Message = _localizer.Resource_MovingTaskSummary(
                        resources.Select(r => r.Value.DisplayName).ToArray(), mediaLibrary?.Name, model.Path);
                    foreach (var id in model.Ids)
                    {
                        await _resourceTaskManager.Add(new ResourceTaskInfo
                        {
                            BackgroundTaskId = bt.Id,
                            Id = id,
                            Type = ResourceTaskType.Moving,
                            Summary = _localizer.Resource_MovingTaskSummary(null, mediaLibrary?.Name, model.Path),
                            OperationOnComplete = ResourceTaskOperationOnComplete.RemoveOnResourceView
                        });
                    }

                    foreach (var (id, resource) in resources)
                    {
                        if (resource.IsSingleFile)
                        {
                            await FileUtils.MoveAsync(resource.RawFullname, model.Path, false,
                                async p => { await _resourceTaskManager.Update(id, t => t.Percentage = p); },
                                bt.Cts.Token);
                        }
                        else
                        {
                            await DirectoryUtils.MoveAsync(resource.RawFullname, model.Path, false,
                                async p => { await _resourceTaskManager.Update(id, t => t.Percentage = p); },
                                bt.Cts.Token);
                        }

                        // var p = 0;
                        // while (p < 100)
                        // {
                        //     p++;
                        //     await Task.Delay(100, bt.Cts.Token);
                        //     await _resourceTaskManager.Update(id, t => t.Percentage = p);
                        // }

                        if (mediaLibrary != null && resource.MediaLibraryId != mediaLibrary.Id)
                        {
                            // todo: simpler way to change base info of resources.
                            var resourceDto = (await resourceService.GetByKey(id, ResourceAdditionalItem.All, true))!;
                            resourceDto.MediaLibraryId = mediaLibrary.Id;
                            resourceDto.CategoryId = mediaLibrary.CategoryId;
                            await resourceService.AddOrUpdateRange(new List<ResourceDto> {resourceDto});
                        }

                        await _resourceTaskManager.Clear(id);
                    }

                    return BaseResponseBuilder.Ok;
                }, BackgroundTaskLevel.Default, null, null,
                async task => await _resourceTaskManager.Update(resources.Keys.ToArray(), t => t.Error = task.Message));

            // await DirectoryUtils.Move(model.EntryPaths.ToDictionary(t => t, t => Path.Combine(model.DestDir, Path.GetFileName(t))), false, )

            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/task")]
        [SwaggerOperation(OperationId = "ClearResourceTask")]
        public async Task<BaseResponse> ClearTask(int id)
        {
            await _resourceTaskManager.Clear(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("{id}/cover")]
        [SwaggerOperation(OperationId = "SaveCover")]
        public async Task<BaseResponse> SaveCover(int id, [FromBody] CoverSaveRequestModel model)
        {
            var resource = await _service.GetByKey(id, ResourceAdditionalItem.None, true);
            if (resource == null)
            {
                return BaseResponseBuilder.BuildBadRequest(_localizer.Resource_NotFound(id));
            }

            if (resource.IsSingleFile && model.SaveToResourceDirectory)
            {
                return BaseResponseBuilder.BuildBadRequest(_localizer.Resource_CoverMustBeInDirectory());
            }

            if (model.SaveToResourceDirectory)
            {
                if (Directory.Exists(resource.RawFullname))
                {
                    var coverFileFullnamePrefix = Path.Combine(resource.RawFullname, "cover");
                    var currentCoverFileFullname = Directory.GetFiles(resource.RawFullname)
                        .FirstOrDefault(t => t.StartsWith(coverFileFullnamePrefix));
                    if (currentCoverFileFullname != null)
                    {
                        if (!model.Overwrite)
                        {
                            return BaseResponseBuilder.Conflict;
                        }

                        FileUtils.Delete(currentCoverFileFullname, false, true);
                    }

                    var data = model.Base64Image.Split(',')[1];
                    var bytes = Convert.FromBase64String(data);
                    var newCoverFileFullname = $"{coverFileFullnamePrefix}.png";
                    await FileUtils.Save(newCoverFileFullname, bytes);
                    CoverCache.Set(id.ToString(), bytes, _coverCacheItemPolicy);
                    return BaseResponseBuilder.Ok;
                }
                else
                {
                    return BaseResponseBuilder.BuildBadRequest(_localizer.PathIsNotFound(resource.RawFullname));
                }
            }
            else
            {
                var data = model.Base64Image.Split(',')[1];
                var bytes = Convert.FromBase64String(data);
                var path = await _tempFileManager.SaveCover(id, new MemoryStream(bytes), HttpContext.RequestAborted);
                return BaseResponseBuilder.Ok;
            }
        }

        [HttpGet("favorites-mappings")]
        [SwaggerOperation(OperationId = "GetFavoritesResourcesMappings")]
        public async Task<SingletonResponse<Dictionary<int, int[]>>> GetFavoritesResourcesMappings(int[] ids)
        {
            var mappings = await _favoritesResourceMappingService.GetAll(t => ids.Contains(t.ResourceId));
            var dict = mappings.GroupBy(t => t.FavoritesId)
                .ToDictionary(t => t.Key, t => t.Select(b => b.ResourceId).ToArray());
            return new SingletonResponse<Dictionary<int, int[]>>(dict);
        }

        [HttpPost("nfo")]
        [SwaggerOperation(OperationId = "StartResourceNfoGenerationTask")]
        public async Task<BaseResponse> StartNfoGenerationTask()
        {
            await _service.TryToGenerateNfoInBackground();
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("{id}/previewer")]
        [SwaggerOperation(OperationId = "GetResourceDataForPreviewer")]
        public async Task<ListResponse<PreviewerItem>> GetResourceDataForPreviewer(int id)
        {
            var resource = await _service.GetByKey(id, false);

            var filePaths = new List<string>();
            if (System.IO.File.Exists(resource.RawFullname))
            {
                filePaths.Add(resource.RawFullname);
            }
            else
            {
                if (Directory.Exists(resource.RawFullname))
                {
                    filePaths.AddRange(Directory.GetFiles(resource.RawFullname, "*", SearchOption.AllDirectories));
                }
                else
                {
                    return ListResponseBuilder<PreviewerItem>.NotFound;
                }
            }

            var items = new List<PreviewerItem>();
            var ffmpegIsReady = _ffMpegHelper.IsReady();

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
                                    (await _ffMpegHelper.GetDuration(f, HttpContext.RequestAborted))),
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
    }
}