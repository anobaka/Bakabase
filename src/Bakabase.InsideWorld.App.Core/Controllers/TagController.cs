using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/tag")]
    public class TagController
    {
        private readonly TagService _service;

        public TagController(TagService service)
        {
            _service = service;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllTags")]
        public async Task<ListResponse<TagDto>> GetAll(TagAdditionalItem additionalItems)
        {
            return new(await _service.GetAll(additionalItems));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddTags")]
        public async Task<BaseResponse> Add([FromBody] Dictionary<int, string[]> model)
        {
            await _service.AddRange(model, false);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("{id:int}/name")]
        [SwaggerOperation(OperationId = "UpdateTagName")]
        public async Task<BaseResponse> UpdateName(int id, [FromBody] TagNameUpdateRequestModel model)
        {
            return await _service.UpdateName(id, model.Name);
        }

        [HttpPatch("{id:int}")]
        [SwaggerOperation(OperationId = "UpdateTag")]
        public async Task<BaseResponse> Update(int id, [FromBody] TagUpdateRequestModel model)
        {
            return await _service.Update(id, model);
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "RemoveTag")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpPut("{id:int}/move")]
        [SwaggerOperation(OperationId = "MoveTag")]
        public async Task<BaseResponse> Move(int id, [FromBody] TagMoveRequestModel model)
        {
            return await _service.Move(id, model);
        }

        [HttpDelete("bulk")]
        [SwaggerOperation(OperationId = "BulkDeleteTags")]
        public async Task<BaseResponse> BulkDelete([FromBody] int[] ids)
        {
            return await _service.RemoveByKeys(ids);
        }
    }
}