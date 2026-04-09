using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Controllers;

public record AddPostParserTasksInput
{
    public Dictionary<int, List<string>> SourceLinksMap { get; set; } = new();
    public List<PostParseTarget> Targets { get; set; } = [];
}

public record QueryPostParserTaskStatusesInput
{
    public PostParserSource Source { get; set; }
    public List<string> Links { get; set; } = [];
}

[ApiController]
[Route("~/post-parser")]
public class PostParserController(
    IPostParserTaskService service,
    IBakabaseLocalizer localizer,
    BTaskManager btm,
    PostParserTaskTrigger trigger) : ControllerBase
{
    [HttpGet("targets")]
    [SwaggerOperation(OperationId = "GetPostParseTargets")]
    public ListResponse<PostParseTarget> GetTargets()
    {
        var targets = Enum.GetValues<PostParseTarget>().ToList();
        return new ListResponse<PostParseTarget>(targets);
    }

    [HttpGet("task/all")]
    [SwaggerOperation(OperationId = "GetAllPostParserTasks")]
    public async Task<ListResponse<PostParserTask>> GetAll()
    {
        var result = await service.GetAll();
        return new ListResponse<PostParserTask>(result);
    }

    [HttpPost("task")]
    [SwaggerOperation(OperationId = "AddPostParserTasks")]
    public async Task<BaseResponse> AddRange([FromBody] AddPostParserTasksInput input)
    {
        await service.AddRange(
            input.SourceLinksMap.ToDictionary(d => (PostParserSource) d.Key, d => d.Value),
            input.Targets);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("task/{id:int}")]
    [SwaggerOperation(OperationId = "DeletePostParserTask")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("task/all")]
    [SwaggerOperation(OperationId = "DeleteAllPostParserTasks")]
    public async Task<BaseResponse> DeleteAll()
    {
        await service.DeleteAll();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("start")]
    [SwaggerOperation(OperationId = "StartAllPostParserTasks")]
    public async Task<BaseResponse> StartParsingAll()
    {
        await trigger.Start();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("task/statuses")]
    [SwaggerOperation(OperationId = "GetPostParserTaskStatuses")]
    public async Task<SingletonResponse<Dictionary<string, PostParserTaskStatus>>> GetTaskStatuses([FromBody] QueryPostParserTaskStatusesInput input)
    {
        var result = await service.GetStatusesByLinks(input.Source, input.Links);
        return new SingletonResponse<Dictionary<string, PostParserTaskStatus>>(result);
    }
}
