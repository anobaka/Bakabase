using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public record PostParserAcquisitionInput
{
    [MaxLength(1024)] public string? Title { get; init; }
    [Required] public List<int> ResourceIndices { get; init; } = [];
    public int Revision { get; init; }
}

[ApiController]
[Route("~/post-parser/task/{id:int}/acquisition")]
public class PostParserAcquisitionController(PostParserAcquisitionService service) : ControllerBase
{
    [HttpPost("~/post-parser/task/{id:int}/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryPostParserTaskWorkflow")]
    public async Task<BaseResponse> Retry(int id, [FromServices] IPostParserTaskService tasks)
    {
        await tasks.Retry(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ImportPostParserTaskToAcquisition")]
    public async Task<SingletonResponse<PostParserAcquisitionResult>> Import(int id,
        [FromBody] PostParserAcquisitionInput input, CancellationToken ct)
    {
        try { return new(await service.ImportAsync(id, input.Revision, input.Title, input.ResourceIndices, ct)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return SingletonResponseBuilder<PostParserAcquisitionResult>.BuildBadRequest(ex.Message); }
    }
}
