using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/publisher")]
    public class PublisherController
    {
        private readonly PublisherService _service;

        public PublisherController(PublisherService service)
        {
            _service = service;
        }

        [HttpPut("{id:int}")]
        [SwaggerOperation(OperationId = "UpdatePublisher")]
        public async Task<BaseResponse> Update(int id, [FromBody] PublisherUpdateModel model)
        {
            await _service.Update(id, model);
            return BaseResponseBuilder.Ok;
        }
    }
}