using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Authoring and viewing profiles saved here for a source already explicitly connected to this device.</summary>
[ApiController]
[Route("resource-profile/sources")]
public sealed class ResourceProfileSourcesController(SourceResourceProfileService sources) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileSources")]
    public async Task<ListResponse<ResourceProfileSourceViewModel>> Sources(CancellationToken ct) =>
        new(await sources.SourcesAsync(ct));

    [HttpGet("{deviceId}/info")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetConnectedResourceProfileSourceInfo")]
    public async Task<SingletonResponse<ResourceProfileSourceInfo>> Info(string deviceId,
        [FromQuery] string? libraryEpoch = null, CancellationToken ct = default) =>
        new(await sources.InfoAsync(deviceId, libraryEpoch, ct));

    [HttpGet("{deviceId}/properties")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetSourceResourceProfileProperties")]
    public Task<ListResponse<PropertyViewModel>> Properties(string deviceId,
        [FromQuery] string? libraryEpoch = null, CancellationToken ct = default) =>
        sources.ReadAsync<ListResponse<PropertyViewModel>>(deviceId, "properties", null, libraryEpoch, ct);

    [HttpPost("{deviceId}/filter-value")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetSourceResourceProfileFilterValue")]
    public Task<SingletonResponse<PropertyViewModel>> FilterValue(string deviceId,
        [FromBody] ResourceSearchFilterInputModel input, [FromQuery] string? libraryEpoch = null,
        CancellationToken ct = default) =>
        sources.ReadAsync<SingletonResponse<PropertyViewModel>>(deviceId, "filter-value", input, libraryEpoch, ct);

    [HttpPost("{deviceId}/search/resolve")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ResolveSourceResourceProfileSearch")]
    public Task<SingletonResponse<ResourceSearchViewModel>> ResolveSearch(string deviceId,
        [FromBody] ResourceSearchInputModel input, [FromQuery] string? libraryEpoch = null,
        CancellationToken ct = default) =>
        sources.ReadAsync<SingletonResponse<ResourceSearchViewModel>>(deviceId, "search-resolve", input, libraryEpoch, ct);

    [HttpPost("{deviceId}/preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewSourceResourceProfileSearch")]
    public Task<SearchResponse<ResourceProfilePreviewResource>> Preview(string deviceId,
        [FromBody] ResourceSearchInputModel input, [FromQuery] string? libraryEpoch = null,
        CancellationToken ct = default) =>
        sources.ReadAsync<SearchResponse<ResourceProfilePreviewResource>>(deviceId, "preview", input, libraryEpoch, ct);

}
