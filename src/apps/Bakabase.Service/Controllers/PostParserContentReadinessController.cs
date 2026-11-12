using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/post-parser/task/{id:int}/content-readiness")]
public sealed class PostParserContentReadinessController(PostParserWorkflowService<BakabaseDbContext> workflow) : ControllerBase
{
    [HttpGet, RemoteAccessible, SwaggerOperation(OperationId = "GetPostParserContentReadiness")]
    public async Task<SingletonResponse<PostParserContentReadinessView>> Get(int id, CancellationToken ct)
    {
        try { return new(await workflow.GetContentReadinessAsync(id, ct)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { return SingletonResponseBuilder<PostParserContentReadinessView>.BuildBadRequest(e.Message); }
    }

    [HttpPost("confirm"), RemoteAccessible, SwaggerOperation(OperationId = "ConfirmPostParserContentReadiness")]
    public async Task<SingletonResponse<PostParserContentReadinessView>> Confirm(int id,
        [FromBody] ConfirmPostParserContentReadinessInput input, CancellationToken ct)
    {
        try { return new(await workflow.ConfirmContentReadinessAsync(id, input, ct)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { return SingletonResponseBuilder<PostParserContentReadinessView>.BuildBadRequest(e.Message); }
    }

    [HttpPost("revoke"), RemoteAccessible, SwaggerOperation(OperationId = "RevokePostParserContentReadiness")]
    public async Task<SingletonResponse<PostParserContentReadinessView>> Revoke(int id,
        [FromBody] RevokePostParserContentReadinessInput input, CancellationToken ct)
    {
        try { return new(await workflow.RevokeContentReadinessAsync(id, input, ct)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { return SingletonResponseBuilder<PostParserContentReadinessView>.BuildBadRequest(e.Message); }
    }

    [HttpPost("refresh"), RemoteAccessible, SwaggerOperation(OperationId = "RefreshPostParserContentReadiness")]
    public async Task<SingletonResponse<PostParserContentReadinessView>> Refresh(int id,
        [FromBody] RefreshPostParserContentReadinessInput input, CancellationToken ct)
    {
        try { return new(await workflow.RefreshContentReadinessAsync(id, input, ct)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        { return SingletonResponseBuilder<PostParserContentReadinessView>.BuildBadRequest(e.Message); }
    }
}
