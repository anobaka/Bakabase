using Bakabase.InsideWorld.Business.Components.ThirdParty.Services;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/third-party")]
    public class ThirdPartyController(IThirdPartyService service) : Controller
    {
        [HttpGet("request-statistics")]
        [SwaggerOperation(OperationId = "GetAllThirdPartyRequestStatistics")]
        public SingletonResponse<ThirdPartyRequestStatistics[]> GetAllRequestStatistics()
        {
            return new SingletonResponse<ThirdPartyRequestStatistics[]>(service.GetAllThirdPartyRequestStatistics());
        }
    }
}