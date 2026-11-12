using System;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/runtime-overviews")]
public sealed class AcquisitionRuntimeOverviewsController(AcquisitionRuntimeOverviewService service) : ControllerBase
{
    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionRuntimeOverviews")]
    public async Task<ListResponse<AcquisitionRuntimeOverview>> Get([FromBody] AcquisitionRuntimeOverviewInput input)
    {
        try { return new(await service.GetAsync(input.TaskIds, HttpContext.RequestAborted)); }
        catch (ArgumentException error) { return ListResponseBuilder<AcquisitionRuntimeOverview>.BuildBadRequest(error.Message); }
    }
}
