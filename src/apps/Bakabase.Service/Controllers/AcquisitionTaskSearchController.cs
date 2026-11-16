using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks/search")]
public sealed class AcquisitionTaskSearchController(AcquisitionTaskSearchService service) : ControllerBase
{
    [HttpPost("~/acquisition/tasks/names")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionTaskNames")]
    public async Task<SingletonResponse<Dictionary<int, string>>> Names([FromBody] AcquisitionTaskNamesInput input)
    {
        try { return new(await service.GetNamesAsync(input, HttpContext.RequestAborted)); }
        catch (ArgumentException error) { return SingletonResponseBuilder<Dictionary<int, string>>.BuildBadRequest(error.Message); }
    }

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SearchAcquisitionTasks")]
    public async Task<SingletonResponse<AcquisitionTaskPage>> Search([FromBody] AcquisitionTaskSearchInput input)
    {
        try { return new(await service.SearchAsync(input, HttpContext.RequestAborted)); }
        catch (ArgumentException error) { return SingletonResponseBuilder<AcquisitionTaskPage>.BuildBadRequest(error.Message); }
    }
}
