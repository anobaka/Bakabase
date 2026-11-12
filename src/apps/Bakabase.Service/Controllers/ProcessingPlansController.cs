using System;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/processing-plans")]
public sealed class ProcessingPlansController(SourceProcessingPlanService service) : ControllerBase
{
    [HttpPost("source/query")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetSourceProcessingPlan")]
    public Task<SingletonResponse<SourceProcessingPlanView>> Get([FromBody] WorkflowSourceRef source) =>
        Respond(() => service.GetAsync(source, HttpContext.RequestAborted));

    [HttpPost("revisions")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ReviseSourceProcessingPlan")]
    public Task<SingletonResponse<SourceProcessingPlanView>> Revise([FromBody] ProcessingPlanRevisionInput input) =>
        Respond(() => service.ReviseAsync(input, HttpContext.RequestAborted));

    private static async Task<SingletonResponse<SourceProcessingPlanView>> Respond(Func<Task<SourceProcessingPlanView>> action)
    {
        try { return new(await action()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return SingletonResponseBuilder<SourceProcessingPlanView>.BuildBadRequest(error.Message); }
    }
}
