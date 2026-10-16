using System;
using System.Threading.Tasks;
using System.Threading;
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
        ?? throw new InvalidOperationException("Workspaces are managed in the Bakabase desktop application.");

    // No RemoteAccessible: profile paths and process launch are local-machine capabilities.
    [HttpGet]
    [SwaggerOperation(OperationId = "GetDesktopProfiles")]
    public SingletonResponse<DesktopProfilesView> Get() => new(HttpContext.RequestServices.GetService<IDesktopProfileService>()?.Get()
        ?? new DesktopProfilesView(false, null, false, null, []));

    [HttpPost]
    [RunsOnUserMachine(Reason = "Workspaces register local desktop data directories.")]
    [SwaggerOperation(OperationId = "AddDesktopProfile")]
    public SingletonResponse<DesktopProfileView> Add([FromBody] DesktopProfileAddInput input) => new(Profiles.Add(input));

    [HttpPut("{id}")]
    [RunsOnUserMachine(Reason = "Workspaces configure this desktop application's launch registry.")]
    [SwaggerOperation(OperationId = "UpdateDesktopProfile")]
    public SingletonResponse<DesktopProfileView> Update(string id, [FromBody] DesktopProfileUpdateInput input) => new(Profiles.Update(id, input));

    [HttpDelete("{id}")]
    [RunsOnUserMachine(Reason = "Removing a workspace changes this desktop application's launch registry.")]
    [SwaggerOperation(OperationId = "RemoveDesktopProfile")]
    public BaseResponse Remove(string id) { Profiles.Remove(id); return BaseResponseBuilder.Ok; }

    [HttpPost("{id}/open")]
    [RunsOnUserMachine(Reason = "Opening a workspace activates or launches a local desktop process.")]
    [SwaggerOperation(OperationId = "OpenDesktopProfile")]
    public async Task<BaseResponse> Open(string id, [FromBody] DesktopProfileOpenInput input)
    {
        var profiles = Profiles;
        if (await profiles.OpenAsync(id, input.CloseCurrent))
        {
            var committed = 0;
            Task Commit() => Interlocked.Exchange(ref committed, 1) == 0
                ? profiles.CompleteOpenAsync(id) : Task.CompletedTask;
            // Complete the response before stopping its host. A disconnected browser must
            // not leave an accepted switch pending indefinitely.
            Response.OnCompleted(Commit);
            HttpContext.RequestAborted.Register(() => _ = Commit());
        }
        return BaseResponseBuilder.Ok;
    }
}
