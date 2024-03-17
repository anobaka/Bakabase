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
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using ElectronNET.API.Entities;
using Google.Apis.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Utilities;
using SharpCompress.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Swashbuckle.AspNetCore.Annotations;
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
		private readonly FavoritesResourceMappingService _favoritesResourceMappingService;
		private readonly IWebHostEnvironment _env;
		private readonly InsideWorldOptionsManagerPool _insideWorldOptionsManager;
		private readonly InsideWorldLocalizer _localizer;
		private readonly FfMpegService _ffMpegService;
		private readonly TempFileManager _tempFileManager;
		private readonly IBOptions<ResourceOptions> _resourceOptions;
		private readonly Business.Components.Dependency.Implementations.FfMpeg.FfMpegService _ffMpegInstaller;
		private readonly ILogger<ResourceController> _logger;
		private readonly OriginalService _originalService;
		private readonly SeriesService _seriesService;
		private readonly CustomPropertyValueService _customPropertyValueService;

		public ResourceController(ResourceService service,
			ResourceTagMappingService resourceTagMappingService,
			IServiceProvider serviceProvider, CustomResourcePropertyService customResourcePropertyService,
			SpecialTextService specialTextService, MediaLibraryService mediaLibraryService,
			ResourceTaskManager resourceTaskManager, BackgroundTaskManager taskManager,
			FavoritesResourceMappingService favoritesResourceMappingService, IWebHostEnvironment env,
			InsideWorldOptionsManagerPool insideWorldOptionsManager, InsideWorldLocalizer localizer,
			FfMpegService ffMpegService, TempFileManager tempFileManager, IBOptions<ResourceOptions> resourceOptions,
			Business.Components.Dependency.Implementations.FfMpeg.FfMpegService ffMpegInstaller,
			ILogger<ResourceController> logger, OriginalService originalService, SeriesService seriesService,
			CustomPropertyValueService customPropertyValueService)
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
			_ffMpegService = ffMpegService;
			_tempFileManager = tempFileManager;
			_resourceOptions = resourceOptions;
			_ffMpegInstaller = ffMpegInstaller;
			_logger = logger;
			_originalService = originalService;
			_seriesService = seriesService;
			_customPropertyValueService = customPropertyValueService;
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
		[ResponseCache(Duration = 20 * 60)]
		public async Task<IActionResult> GetCover(int id)
		{
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

				byte[]? data = null;
				string? mimeType = null;
				try
				{
					var image = await Image.LoadAsync<Rgb24>(ms, HttpContext.RequestAborted);
					if (image.Width >= 800 || image.Height >= 800)
					{
						var scale = Math.Min(800m / image.Width, 800m / image.Height);
						image.Mutate(t => t.Resize((int) (image.Width * scale), (int) (image.Height * scale)));
						data = await image.SaveAsync(JpegFormat.Instance);
						mimeType = MimeTypes.GetMimeType(".jpg");
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $"An error occurred during loading cover: {ex.Message}");
				}

				data ??= ms.ToArray();
				mimeType ??= MimeTypes.GetMimeType(ext);

				return File(data, mimeType);
			}

			return NotFound();
		}

		[HttpGet("{id}/playable-files")]
		[SwaggerOperation(OperationId = "GetResourcePlayableFiles")]
		[ResponseCache(Duration = 20 * 60)]
		public async Task<ListResponse<string>> GetPlayableFiles(int id)
		{
			return new ListResponse<string>(await _service.GetPlayableFiles(id, HttpContext.RequestAborted) ??
			                                new string[] { });
		}

		[HttpGet("custom-property-keys")]
		[SwaggerOperation(OperationId = "GetAllCustomPropertyKeys")]
		public async Task<ListResponse<string>> GetAllCustomPropertyKeys()
		{
			var data = await _customResourcePropertyService.GetAllKeys();
			return new(data);
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
							await resourceService.AddOrPatchRange(new List<ResourceDto> {resourceDto});
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
			var rsp = await _service.SaveCover(id, model.SaveLocation, model.Overwrite, () =>
			{
				var data = model.Base64Image.Split(',')[1];
				var bytes = Convert.FromBase64String(data);
				return bytes;
			}, HttpContext.RequestAborted);
			// if (rsp.Code == (int) ResponseCode.Success)
			// {
			//     CoverCache.Set(id.ToString(), rsp.Data.Data, CoverCacheItemPolicy);
			// }

			return rsp;
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

			if (resource == null)
			{
				return ListResponseBuilder<PreviewerItem>.NotFound;
			}

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

		[HttpGet("original/all")]
		[SwaggerOperation(OperationId = "GetAllOriginals")]
		public async Task<ListResponse<OriginalDto>> GetAllOriginals()
		{
			return new ListResponse<OriginalDto>(await _originalService.GetAllDtoList(null, false));
		}

		[HttpGet("series/all")]
		[SwaggerOperation(OperationId = "GetAllSeries")]
		public async Task<ListResponse<SeriesDto>> GetAllSeries()
		{
			return new ListResponse<SeriesDto>(await _seriesService.GetAll());
		}

		[HttpGet("custom-property/all")]
		[SwaggerOperation(OperationId = "GetAllCustomProperties")]
		public async Task<SingletonResponse<Dictionary<string, List<CustomResourceProperty>>>> GetAllCustomProperties()
		{
			var cps = await _customResourcePropertyService.GetAll();
			var map = cps.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.ToList());

			return new(map);
		}

		[HttpPut("{id:int}/custom-property/{pId:int}/value")]
		[SwaggerOperation(OperationId = "PutResourceCustomPropertyValue")]
		public async Task<BaseResponse> PutCustomPropertyValue(int id, int pId,
			[FromBody] ResourceCustomPropertyValuePutRequestModel model)
		{
			return await _customPropertyValueService.SetResourceValue(id, pId, model);
		}
	}
}