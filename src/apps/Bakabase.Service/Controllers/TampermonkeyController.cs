using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.Constants;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/[controller]")]
public class TampermonkeyController(TampermonkeyService service, IServerSelfDescription? self = null) : ControllerBase
{
    [HttpGet("health")]
    [SwaggerOperation(OperationId = "TampermonkeyHealth")]
    public BaseResponse Health()
    {
        return BaseResponseBuilder.Ok;
    }

    [RunsOnUserMachine(Reason = "Installing the userscript opens the browser on your machine.", HasBrowserFallback = true)]
    [HttpGet("install")]
    [SwaggerOperation(OperationId = "InstallTampermonkeyScript")]
    public async Task<ActionResult<BaseResponse>> Install()
    {
        if (self?.Kind == ServerKind.Desktop && AppRuntime.Mode != RuntimeMode.Docker &&
            HttpContext.GetRemoteAccessContext() is {IsLoopback: true})
        {
            await service.Install();
            return BaseResponseBuilder.Ok;
        }

        // Let the browser supply its actual origin, including HTTPS terminated by a
        // reverse proxy. Do not trust forwarded headers or launch anything on the host.
        Response.Headers.CacheControl = "no-store";
        return Content("""
            <!doctype html><meta charset="utf-8"><title>Bakabase userscript</title>
            <script>
            const url = new URL(window.location.href);
            url.pathname = url.pathname.replace(/\/install\/?$/i, '/script/bakabase.user.js');
            url.search = '';
            url.hash = '';
            url.searchParams.set('apiEndpoint', window.location.origin);
            window.location.replace(url.href);
            </script>
            """, "text/html");
    }

    [HttpGet("script/bakabase.user.js")]
    [SwaggerOperation(OperationId = "GetTampermonkeyScript")]
    public async Task<IActionResult> GetScript([FromQuery] string? apiEndpoint = null)
    {
        var requestedEndpoint = Request.Query.ContainsKey("apiEndpoint") || apiEndpoint != null
            ? apiEndpoint
            : $"{Request.Scheme}://{Request.Host}";
        if (!TampermonkeyService.TryNormalizeOrigin(requestedEndpoint, out var origin))
        {
            return Problem("The API endpoint must be an HTTP or HTTPS origin with no credentials, path, query or fragment.",
                statusCode: 400);
        }

        var js = await service.GetScript(origin!);
        if (string.IsNullOrEmpty(js))
        {
            return Problem("The userscript could not be downloaded. Please try again later.", statusCode: 503);
        }

        Response.Headers.CacheControl = "no-store";
        return Content(js, "application/javascript");
    }
}
