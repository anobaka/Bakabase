using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/[controller]")]
public class TampermonkeyController(TampermonkeyService service) : ControllerBase
{
    [HttpGet("install")]
    [SwaggerOperation(OperationId = "InstallTampermonkeyScript")]
    public async Task<BaseResponse> Install()
    {
        await service.Install();
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("script/bakabase.user.js")]
    [SwaggerOperation(OperationId = "GetTampermonkeyScript")]
    public async Task<IActionResult> GetScript()
    {
        var js = await service.GetScript();
        if (string.IsNullOrEmpty(js))
        {
            return Redirect(TampermonkeyService.ScriptCdnUrl);
        }

        return Content(js, "application/javascript");
    }
}
