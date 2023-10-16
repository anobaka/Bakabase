using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<ListResponse<MediaLibraryDto>> Get()
        {
            return new(await _service.GetAll());
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddMediaLibrary")]
        public async Task<BaseResponse> Add([FromBody] MediaLibraryCreateRequestModel model)
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
        public async Task<BaseResponse> Patch(int id, [FromBody] MediaLibraryUpdateRequestModel model)
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
            [FromBody] MediaLibrary.PathConfiguration pathConfiguration)
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
            [FromBody] MediaLibrary.PathConfiguration configuration)
        {
            if (!Directory.Exists(configuration?.Path))
            {
                return BaseResponseBuilder.BuildBadRequest($"{configuration?.Path} does not exist");
            }

            var library = await _service.GetByKey(id);
            var dto = library.ToDto();
            var pc = dto.PathConfigurations.Concat(new[] {configuration}).ToArray();
            return await _service.Patch(id, new MediaLibraryUpdateRequestModel
            {
                PathConfigurations = pc
            });
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

            var library = await _service.GetByKey(id);
            var dto = library.ToDto();
            if (dto.PathConfigurations != null)
            {
                dto.PathConfigurations = dto.PathConfigurations.Where((t, i) => i != model.Index).ToArray();
            }

            return await _service.Patch(id, new MediaLibraryUpdateRequestModel
            {
                PathConfigurations = dto.PathConfigurations?.Cast<MediaLibrary.PathConfiguration>().ToArray()
            });
        }

        [HttpGet("path-related-libraries")]
        [SwaggerOperation(OperationId = "GetPathRelatedLibraries")]
        public async Task<ListResponse<MediaLibraryDto>> GetPathRelativeLibraries(int libraryId, string currentPath,
            string newPath)
        {
            var libraries = await _service.GetAll();
            var currentUri = currentPath.IsNotEmpty()
                ? MediaLibraryService.StandardizeForPathComparing(currentPath)
                : null;
            var conflictLibraries = libraries.Where(a =>
            {
                var allPcs = new List<MediaLibraryDto.PathConfigurationDto>(a.PathConfigurations);
                if (libraryId == a.Id)
                {
                    var removed = a.PathConfigurations?.FirstOrDefault(b =>
                        MediaLibraryService.StandardizeForPathComparing(b.Path).Equals(currentUri));
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
            return new ListResponse<MediaLibraryDto>(conflictLibraries);
        }
    }
}