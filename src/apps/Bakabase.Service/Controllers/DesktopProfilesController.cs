using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("app/profiles")]
public sealed class DesktopProfilesController : ControllerBase
{
    private IDesktopProfileService Profiles => HttpContext.RequestServices.GetService<IDesktopProfileService>()
        ?? throw new InvalidOperationException("Data spaces are managed in the Bakabase desktop application.");

    // No RemoteAccessible: profile paths and process launch are local-machine capabilities.
    [HttpGet]
    [SwaggerOperation(OperationId = "GetDesktopProfiles")]
    public SingletonResponse<DesktopProfilesView> Get() => new(HttpContext.RequestServices.GetService<IDesktopProfileService>()?.Get()
        ?? new DesktopProfilesView(false, null, false, null, []));

    [HttpPost]
    [RunsOnUserMachine(Reason = "Data spaces register local desktop data directories.")]
    [SwaggerOperation(OperationId = "AddDesktopProfile")]
    public SingletonResponse<DesktopProfileView> Add([FromBody] DesktopProfileAddInput input) => new(Profiles.Add(input));

    [HttpPut("{id}")]
    [RunsOnUserMachine(Reason = "Data spaces configure this desktop application's launch registry.")]
    [SwaggerOperation(OperationId = "UpdateDesktopProfile")]
    public SingletonResponse<DesktopProfileView> Update(string id, [FromBody] DesktopProfileUpdateInput input) => new(Profiles.Update(id, input));

    [HttpDelete("{id}")]
    [RunsOnUserMachine(Reason = "Removing a data space changes this desktop application's launch registry.")]
    [SwaggerOperation(OperationId = "RemoveDesktopProfile")]
    public BaseResponse Remove(string id) { Profiles.Remove(id); return BaseResponseBuilder.Ok; }

    [HttpPost("{id}/open")]
    [RunsOnUserMachine(Reason = "Opening a data space activates or launches a local desktop process.")]
    [SwaggerOperation(OperationId = "OpenDesktopProfile")]
    public async Task<BaseResponse> Open(string id, [FromBody] DesktopProfileOpenInput input)
    { await Profiles.OpenAsync(id, input.CloseCurrent); return BaseResponseBuilder.Ok; }
}
