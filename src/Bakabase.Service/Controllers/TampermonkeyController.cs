using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bakabase.InsideWorld.Business.Components.Tampermonkey.Models.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
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
    public async Task<BaseResponse> Install(TampermonkeyScript script)
    {
        await service.Install(script);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("script/{script}.user.js")]
    [SwaggerOperation(OperationId = "GetTampermonkeyScript")]
    public async Task<IActionResult> GetScript(TampermonkeyScript script)
    {
        var js = await service.GetScript(script);
        if (js.IsNullOrEmpty())
        {
            return NotFound();
        }

        return Content(js, "application/javascript");
    }
}