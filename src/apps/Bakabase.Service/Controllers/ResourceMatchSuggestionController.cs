using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// The pairs of resources that might be the same work, and the two answers a person can give.
/// </summary>
[ApiController]
[Route("~/resource/match-suggestion")]
public class ResourceMatchSuggestionController(IResourceMatchSuggestionService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetPendingResourceMatchSuggestions")]
    public async Task<ListResponse<ResourceMatchSuggestion>> GetPending(CancellationToken ct) =>
        new(await service.GetPending(ct));

    [HttpGet("count")]
    [SwaggerOperation(OperationId = "CountPendingResourceMatchSuggestions")]
    public async Task<SingletonResponse<int>> CountPending(CancellationToken ct) =>
        new(await service.CountPending(ct));

    /// <summary>
    /// Says the two are one work. The newly found resource's identities, leads and memberships move
    /// onto the one that was already here, and it goes away.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    [SwaggerOperation(OperationId = "ConfirmResourceMatchSuggestion")]
    public async Task<BaseResponse> Confirm(int id, CancellationToken ct)
    {
        try
        {
            await service.Confirm(id, ct);

            return BaseResponseBuilder.Ok;
        }
        catch (InvalidOperationException e)
        {
            return BaseResponseBuilder.BuildBadRequest(e.Message);
        }
    }

    [HttpPost("{id:int}/dismiss")]
    [SwaggerOperation(OperationId = "DismissResourceMatchSuggestion")]
    public async Task<BaseResponse> Dismiss(int id, CancellationToken ct)
    {
        await service.Dismiss(id, ct);

        return BaseResponseBuilder.Ok;
    }
}
