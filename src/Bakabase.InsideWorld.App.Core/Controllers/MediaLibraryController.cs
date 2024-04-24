using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Dto;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/media-library")]
	public class MediaLibraryController : Controller
	{
		private readonly MediaLibraryService _service;
		private readonly TagService _tagService;
		private readonly TagGroupService _tagGroupService;

		public MediaLibraryController(MediaLibraryService service, TagService tagService,
			TagGroupService tagGroupService)
		{
			_service = service;
			_tagService = tagService;
			_tagGroupService = tagGroupService;
		}

		[HttpGet]
		[SwaggerOperation(OperationId = "GetAllMediaLibraries")]
		public async Task<ListResponse<MediaLibrary>> Get(MediaLibraryAdditionalItem additionalItems)
		{
			return new(await _service.GetAllDto(null, additionalItems));
		}

		[HttpPost]
		[SwaggerOperation(OperationId = "AddMediaLibrary")]
		public async Task<BaseResponse> Add([FromBody] MediaLibraryCreateDto model)
		{
			return await _service.Add(model);
		}

		[HttpDelete("{id}")]
		[SwaggerOperation(OperationId = "RemoveMediaLibrary")]
		public async Task<BaseResponse> Remove(int id)
		{
			return await _service.RemoveByKey(id);
		}

		[HttpPut("{id}")]
		[SwaggerOperation(OperationId = "PatchMediaLibrary")]
		public async Task<BaseResponse> Patch(int id, [FromBody] MediaLibraryPatchDto model)
		{
			return await _service.Patch(id, model);
		}

		[HttpGet("sync/status")]
		[SwaggerOperation(OperationId = "GetMediaLibrarySyncStatus")]
		public async Task<SingletonResponse<BackgroundTaskDto>> GetSyncStatus()
		{
			return new SingletonResponse<BackgroundTaskDto>(_service.SyncTaskInformation);
		}

		[HttpPut("sync")]
		[SwaggerOperation(OperationId = "SyncMediaLibrary")]
		public async Task<BaseResponse> Sync()
		{
			_service.SyncInBackgroundTask();
			return BaseResponseBuilder.Ok;
		}

		[HttpDelete("sync")]
		[SwaggerOperation(OperationId = "StopSyncMediaLibrary")]
		public async Task<BaseResponse> StopSync()
		{
			await _service.StopSync();
			return BaseResponseBuilder.Ok;
		}

		[HttpPost("path-configuration-validation")]
		[SwaggerOperation(OperationId = "ValidatePathConfiguration")]
		public async Task<SingletonResponse<PathConfigurationValidateResult>> ValidatePathConfiguration(
			[FromBody] PathConfiguration pathConfiguration)
		{
			return await _service.Test(pathConfiguration, 100);
		}

		[HttpPut("orders-in-category")]
		[SwaggerOperation(OperationId = "SortMediaLibrariesInCategory")]
		public async Task<BaseResponse> Sort([FromBody] IdBasedSortRequestModel model)
		{
			return await _service.Sort(model.Ids);
		}

		[HttpPost("{id}/path-configuration")]
		[SwaggerOperation(OperationId = "AddMediaLibraryPathConfiguration")]
		public async Task<BaseResponse> AddPathConfiguration(int id,
			[FromBody] MediaLibraryPathConfigurationCreateRequestModel model)
		{
			if (string.IsNullOrEmpty(model.Path))
			{
				return BaseResponseBuilder.BuildBadRequest($"{nameof(model.Path)} can not be empty");
			}

			var newPc = PathConfiguration.CreateDefault(model.Path);
			var library = await _service.GetDto(id, MediaLibraryAdditionalItem.None);
			(library!.PathConfigurations ??= []).Add(newPc);
			return await _service.Put(library);
		}

		[HttpPost("bulk-add/{cId:int}")]
		[SwaggerOperation(OperationId = "AddMediaLibrariesInBulk")]
		public async Task<BaseResponse> AddInBulk(int cId,
			[FromBody] MediaLibraryAddInBulkRequestModel model)
		{
			model.TrimSelf();
			if (!model.NameAndPaths.Any())
			{
				return BaseResponseBuilder.BuildBadRequest($"{nameof(model.NameAndPaths)} are required");
			}

			var libraries = model.NameAndPaths.Select(l =>
			{
				var (name, paths) = l;
				return MediaLibrary.CreateDefault(name, cId, paths);
			}).ToArray();

			await _service.AddRange(libraries);
			return BaseResponseBuilder.Ok;
		}

		[HttpPost("{mlId:int}/path-configuration/root-paths")]
		[SwaggerOperation(OperationId = "AddMediaLibraryRootPathsInBulk")]
		public async Task<BaseResponse> AddPathConfigurationRootPathsInBulk(int mlId,
			[FromBody] MediaLibraryRootPathsAddInBulkRequestModel model)
		{
			model.TrimSelf();

			if (!model.RootPaths.Any())
			{
				return BaseResponseBuilder.BuildBadRequest($"{nameof(model.RootPaths)} are required");
			}

			var library = (await _service.GetDto(mlId, MediaLibraryAdditionalItem.None))!;
			(library.PathConfigurations ??= []).AddRange(model.RootPaths.Select(PathConfiguration.CreateDefault)
				.ToArray());

			return await _service.Put(library);
		}

		[HttpDelete("{id}/path-configuration")]
		[SwaggerOperation(OperationId = "RemoveMediaLibraryPathConfiguration")]
		public async Task<BaseResponse> RemovePathConfiguration(int id,
			[FromBody] PathConfigurationRemoveRequestModel model)
		{
			if (model.Index < 0)
			{
				return BaseResponseBuilder.BadRequestOrOperation;
			}

			var library = (await _service.GetDto(id, MediaLibraryAdditionalItem.None))!;
			library.PathConfigurations = library.PathConfigurations?.Where((t, i) => i != model.Index).ToList();

			return await _service.Put(library);
		}

		[HttpGet("path-related-libraries")]
		[SwaggerOperation(OperationId = "GetPathRelatedLibraries")]
		public async Task<ListResponse<MediaLibrary>> GetPathRelativeLibraries(int libraryId, string currentPath,
			string newPath)
		{
			var libraries = await _service.GetAllDto(null, MediaLibraryAdditionalItem.None);
			var stdPath = currentPath.StandardizePath()!;
			var conflictLibraries = libraries.Where(a =>
			{
				var allPcs = a.PathConfigurations;
				if (allPcs == null)
				{
					return false;
				}

				if (libraryId == a.Id)
				{
					var removed = a.PathConfigurations?.FirstOrDefault(b =>
						b.Path.StandardizePath()?.Equals(stdPath) == true);
					if (removed != null)
					{
						allPcs.Remove(removed);
					}
				}

				if (MediaLibraryService.CheckPathContaining(allPcs.Select(b => b.Path).ToArray(), newPath))
				{
					return true;
				}

				return false;
			}).ToArray();
			return new ListResponse<MediaLibrary>(conflictLibraries);
		}
	}
}