using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using MediaLibrary = Bakabase.Abstractions.Models.Domain.MediaLibrary;

namespace Bakabase.Service.Controllers
{
    [Route("~/media-library")]
    public class MediaLibraryController(IMediaLibraryService service) : Controller
    {
        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllMediaLibraries")]
        public async Task<ListResponse<MediaLibrary>> Get(MediaLibraryAdditionalItem additionalItems)
        {
            return new(await service.GetAll(null, additionalItems));
        }

        [HttpGet("{id:int}")]
        [SwaggerOperation(OperationId = "GetMediaLibrary")]
        public async Task<SingletonResponse<MediaLibrary?>> Get(int id, MediaLibraryAdditionalItem additionalItems)
        {
            return new(await service.Get(id, additionalItems));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddMediaLibrary")]
        public async Task<BaseResponse> Add([FromBody] MediaLibraryAddDto model)
        {
            return await service.Add(model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "DeleteMediaLibrary")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await service.DeleteByKey(id);
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "PatchMediaLibrary")]
        public async Task<BaseResponse> Patch(int id, [FromBody] MediaLibraryPatchDto model)
        {
            return await service.Patch(id, model);
        }

        [HttpPut("sync")]
        [SwaggerOperation(OperationId = "StartSyncMediaLibrary")]
        public async Task<BaseResponse> Sync()
        {
            service.StartSyncing(null, null);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("sync")]
        [SwaggerOperation(OperationId = "StopSyncMediaLibrary")]
        public async Task<BaseResponse> StopSync()
        {
            await service.StopSyncing();
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("path-configuration-validation")]
        [SwaggerOperation(OperationId = "ValidatePathConfiguration")]
        public async Task<SingletonResponse<PathConfigurationTestResult>> ValidatePathConfiguration(
            [FromBody] PathConfiguration pathConfiguration)
        {
            return await service.Test(pathConfiguration, 100);
        }

        [HttpPut("orders-in-category")]
        [SwaggerOperation(OperationId = "SortMediaLibrariesInCategory")]
        public async Task<BaseResponse> Sort([FromBody] IdBasedSortRequestModel model)
        {
            
            return await service.Sort(model.Ids);
        }

        [HttpPost("{id}/path-configuration")]
        [SwaggerOperation(OperationId = "AddMediaLibraryPathConfiguration")]
        public async Task<BaseResponse> AddPathConfiguration(int id,
            [FromBody] MediaLibraryPathConfigurationAddInputModel model)
        {
            if (string.IsNullOrEmpty(model.Path))
            {
                return BaseResponseBuilder.BuildBadRequest($"{nameof(model.Path)} can not be empty");
            }

            var newPc = PathConfiguration.CreateDefault(model.Path);
            var library = await service.Get(id, MediaLibraryAdditionalItem.None);
            (library!.PathConfigurations ??= []).Add(newPc);
            return await service.Put(library);
        }

        [HttpPost("bulk-add/{cId:int}")]
        [SwaggerOperation(OperationId = "AddMediaLibrariesInBulk")]
        public async Task<BaseResponse> AddInBulk(int cId,
            [FromBody] MediaLibraryAddInBulkInputModel model)
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

            await service.AddRange(libraries);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("{mlId:int}/path-configuration/root-paths")]
        [SwaggerOperation(OperationId = "AddMediaLibraryRootPathsInBulk")]
        public async Task<BaseResponse> AddPathConfigurationRootPathsInBulk(int mlId,
            [FromBody] MediaLibraryRootPathsAddInBulkInputModel model)
        {
            model.TrimSelf();

            if (!model.RootPaths.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"{nameof(model.RootPaths)} are required");
            }

            var library = (await service.Get(mlId, MediaLibraryAdditionalItem.None))!;
            (library.PathConfigurations ??= []).AddRange(model.RootPaths.Select(PathConfiguration.CreateDefault)
                .ToArray());

            return await service.Put(library);
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

            var library = (await service.Get(id, MediaLibraryAdditionalItem.None))!;
            library.PathConfigurations = library.PathConfigurations?.Where((t, i) => i != model.Index).ToList();

            return await service.Put(library);
        }

        [HttpPut("{id:int}/synchronization")]
        [SwaggerOperation(OperationId = "StartSyncingMediaLibraryResources")]
        public async Task<BaseResponse> StartSyncing(int id)
        {
            service.StartSyncing(null, [id]);
            return BaseResponseBuilder.Ok;
        }
    }
}