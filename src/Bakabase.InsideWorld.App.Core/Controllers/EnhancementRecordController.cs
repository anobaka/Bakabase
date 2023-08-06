using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/enhancement-record")]
    public class EnhancementRecordController
    {
        private readonly EnhancementRecordService _service;

        public EnhancementRecordController(EnhancementRecordService service)
        {
            _service = service;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "SearchEnhancementRecords")]
        public async Task<SearchResponse<EnhancementRecordDto>> Search(EnhancementRecordSearchRequestModel model)
        {
            var data = await _service.SearchDto(model);
            return data;
        }

        // [HttpGet("enhancer-type")]
        // [SwaggerOperation(OperationId = "GetAllEnhancerTypesFromRecords")]
        // public async Task<ListResponse<string>> GetEnhancerTypes()
        // {
        //     return new ListResponse<string>(await _service.GetAllEnhancerTypes());
        // }
    }
}