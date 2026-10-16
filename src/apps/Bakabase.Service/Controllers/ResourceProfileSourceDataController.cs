using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("resource-profile")]
[ResourceProfileSourceEpoch]
public sealed class ResourceProfileSourceDataController(ResourceProfileSourceReader reader) : ControllerBase
{
    [HttpGet("source-info")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileSourceInfo")]
    public async Task<SingletonResponse<ResourceProfileSourceInfo>> Info(CancellationToken ct) =>
        new(await reader.InfoAsync(ct));

    [HttpGet("search/properties")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileSearchProperties")]
    public Task<ListResponse<PropertyViewModel>> Properties(CancellationToken ct) => reader.PropertiesAsync(ct);

    [HttpPost("search/filter-value")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetResourceProfileSearchFilterValue")]
    public Task<SingletonResponse<PropertyViewModel>> FilterValue([FromBody] ResourceSearchFilterInputModel input,
        CancellationToken ct) => reader.FilterValueAsync(input, ct);

    [HttpPost("preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewResourceProfileSearch")]
    public Task<SearchResponse<ResourceProfilePreviewResource>> Preview([FromBody] ResourceSearchInputModel input,
        CancellationToken ct) => reader.PreviewAsync(input, true, ct);
}
