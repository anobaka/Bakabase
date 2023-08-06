using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/[controller]")]
    [ApiController]
    public class TagGroupController : ControllerBase
    {
        private readonly TagGroupService _service;

        public TagGroupController(TagGroupService service)
        {
            _service = service;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllTagGroups")]
        public async Task<ListResponse<TagGroupDto>> Get(TagGroupAdditionalItem additionalItems)
        {
            return new ListResponse<TagGroupDto>(await _service.GetAll(additionalItems));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddTagGroups")]
        public async Task<BaseResponse> Add([FromBody] TagGroupAddRequestModel model)
        {
            await _service.AddRange(model);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "UpdateTagGroup")]
        public async Task<BaseResponse> Update(int id, [FromBody] TagGroupUpdateRequestModel model)
        {
            return await _service.Update(id, model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveTagGroup")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpPut("orders")]
        [SwaggerOperation(OperationId = "SortTagGroups")]
        public async Task<BaseResponse> Sort([FromBody] IdBasedSortRequestModel model)
        {
            return await _service.Sort(model.Ids);
        }
    }
}