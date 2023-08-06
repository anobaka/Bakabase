using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Extensions;
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
    [Route("~/favorites")]
    public class FavoritesController : Controller
    {
        private readonly FavoritesService _service;
        private readonly FavoritesResourceMappingService _resourceMappingService;

        public FavoritesController(FavoritesService service, FavoritesResourceMappingService resourceMappingService)
        {
            _service = service;
            _resourceMappingService = resourceMappingService;
        }

        [SwaggerOperation(OperationId = "GetAllFavorites")]
        [HttpGet]
        public async Task<ListResponse<FavoritesDto>> GetAll()
        {
            return new ListResponse<FavoritesDto>((await _service.GetAll()).Select(t => t.ToDto()));
        }

        [SwaggerOperation(OperationId = "AddFavorites")]
        [HttpPost]
        public async Task<BaseResponse> Add([FromBody] FavoritesAddOrUpdateRequestModel model)
        {
            return await _service.Add(model);
        }

        [SwaggerOperation(OperationId = "PutFavorites")]
        [HttpPatch("{id}")]
        public async Task<BaseResponse> Put(int id, [FromBody] FavoritesAddOrUpdateRequestModel model)
        {
            return await _service.UpdateByKey(id, t =>
            {
                t.Name = model.Name;
                t.Description = model.Description;
            });
        }

        [SwaggerOperation(OperationId = "DeleteFavorites")]
        [HttpDelete("{id}")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [SwaggerOperation(OperationId = "AddResourceToFavorites")]
        [HttpPost("{id}/resource")]
        public async Task<BaseResponse> AddResource(int id,
            [FromBody] FavoritesResourceMappingAddOrRemoveRequestModel model)
        {
            return await _resourceMappingService.AddRange(new List<FavoritesResourceMapping>
            {
                new FavoritesResourceMapping
                {
                    FavoritesId = id,
                    ResourceId = model.ResourceId
                }
            });
        }

        [SwaggerOperation(OperationId = "DeleteResourceFromFavorites")]
        [HttpDelete("{id}/resource")]
        public async Task<BaseResponse> RemoveResource(int id,
            [FromBody] FavoritesResourceMappingAddOrRemoveRequestModel model)
        {
            var d = await _resourceMappingService.GetFirst(t =>
                t.FavoritesId == id && t.ResourceId == model.ResourceId);
            if (d != null)
            {
                return await _resourceMappingService.Remove(d);
            }

            return BaseResponseBuilder.Ok;
        }

        [SwaggerOperation(OperationId = "PutResourcesFavorites")]
        [HttpPut("resource-mapping")]
        public async Task<BaseResponse> PutResources([FromBody] Dictionary<int, int[]> model)
        {
            return await _resourceMappingService.UpdateResourcesFavoritesMappings(model);
        }
    }
}