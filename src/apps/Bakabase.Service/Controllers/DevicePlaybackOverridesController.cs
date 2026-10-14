using System;
using System.Threading.Tasks;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/player/device-playback/overrides")]
public sealed class DevicePlaybackOverridesController(DevicePlaybackOverridesStore store,
    IPlayerExecutableLocator locator) : Controller
{
    [HttpGet]
    [RunsOnUserMachine(Reason = "Opening rules and installed players belong to the viewing device.")]
    [SwaggerOperation(OperationId = "GetDevicePlaybackOverrides")]
    public async Task<ActionResult<SingletonResponse<DevicePlaybackOverrideSnapshot>>> Get(string target)
    {
        Response.Headers.CacheControl = "no-store";
        try { return new SingletonResponse<DevicePlaybackOverrideSnapshot>(
            await DevicePlaybackOverrides.SnapshotAsync(store, locator, target, HttpContext.RequestAborted)); }
        catch (ArgumentException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }

    [HttpPut]
    [RequestSizeLimit(16 * 1024)]
    [RunsOnUserMachine(Reason = "Opening rules are saved on the viewing device, never on the resource owner.")]
    [SwaggerOperation(OperationId = "SetDevicePlaybackOverrides")]
    [ProducesResponseType(typeof(BaseResponse), 200)]
    public async Task<IActionResult> Put([FromBody] DevicePlaybackOverride rule)
    {
        if (!ModelState.IsValid || rule == null)
            return BadRequest(new { code = 400, message = "Choose valid device opening rules." });
        try
        {
            await store.SaveAsync(rule, HttpContext.RequestAborted);
            return Ok(new { code = 0 });
        }
        catch (ArgumentException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }
}
