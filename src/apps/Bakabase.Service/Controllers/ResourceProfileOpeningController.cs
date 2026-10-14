using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("player")]
public sealed class ResourceProfileOpeningController(SourceResourceProfileService sources) : ControllerBase
{
    [HttpGet("resource-profile")]
    [RunsOnUserMachine(Reason = "Opening profiles belong to the device you are using.", HasBrowserFallback = true)]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileOpeningBehavior")]
    public async Task<SingletonResponse<ResourceProfileOpeningViewModel>> OpeningBehavior(int resourceId,
        [FromServices] IResourceProfileService profiles, [FromServices] IResourceService resources,
        [FromQuery] string? sourceDeviceId = null, [FromQuery] string? libraryEpoch = null,
        CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        var options = sourceDeviceId != null
            ? await sources.ResolveAsync(sourceDeviceId, libraryEpoch, resourceId, ct)
            : (await resources.Get(resourceId) is { } resource
                ? await profiles.GetMatchingProfiles(resource) : []).ResolveOpeningOptions();
        return new(new ResourceProfileOpeningViewModel(options.OpeningMode, options.ProfileIds));
    }

    [HttpPost("resource-profiles")]
    [RunsOnUserMachine(Reason = "Opening profiles belong to the device you are using.", HasBrowserFallback = true)]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileOpeningBehaviors")]
    public async Task<ActionResult<ListResponse<ResourceProfileOpeningResourceViewModel>>> OpeningBehaviors(
        [FromBody] ResourceProfileOpeningInputModel input, [FromServices] IResourceProfileService profiles,
        [FromServices] IResourceService resources, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (input.ResourceIds is not {Length: > 0 and <= 256} || input.ResourceIds.Any(id => id <= 0))
            return BadRequest(new {message = "Provide between 1 and 256 resource IDs."});
        var ids = input.ResourceIds.Distinct().ToArray();
        if (input.SourceDeviceId != null)
        {
            var options = await sources.ResolveManyAsync(input.SourceDeviceId, input.LibraryEpoch, ids, ct);
            return new ListResponse<ResourceProfileOpeningResourceViewModel>(ids.Select(id =>
                new ResourceProfileOpeningResourceViewModel(id, options[id].OpeningMode, options[id].ProfileIds)));
        }
        var local = (await resources.GetByKeys(ids)).ToDictionary(r => r.Id);
        var result = new List<ResourceProfileOpeningResourceViewModel>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var options = (local.TryGetValue(id, out var resource)
                ? await profiles.GetMatchingProfiles(resource) : []).ResolveOpeningOptions();
            result.Add(new(id, options.OpeningMode, options.ProfileIds));
        }
        return new ListResponse<ResourceProfileOpeningResourceViewModel>(result);
    }
}
