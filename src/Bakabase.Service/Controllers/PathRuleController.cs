using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/path-rule")]
public class PathRuleController(IPathRuleService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllPathRules")]
    public async Task<ListResponse<PathRule>> GetAll()
    {
        var items = await service.GetAll();
        return new ListResponse<PathRule>(items);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetPathRule")]
    public async Task<SingletonResponse<PathRule?>> Get(int id)
    {
        var item = await service.Get(id);
        return new SingletonResponse<PathRule?>(item);
    }

    [HttpGet("by-path")]
    [SwaggerOperation(OperationId = "GetPathRuleByPath")]
    public async Task<SingletonResponse<PathRule?>> GetByPath([FromQuery] string path)
    {
        var item = await service.GetByPath(path);
        return new SingletonResponse<PathRule?>(item);
    }

    [HttpGet("applicable")]
    [SwaggerOperation(OperationId = "GetApplicablePathRules")]
    public async Task<ListResponse<PathRule>> GetApplicableRules([FromQuery] string path)
    {
        var items = await service.GetApplicableRules(path);
        return new ListResponse<PathRule>(items);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddPathRule")]
    public async Task<SingletonResponse<PathRule>> Add([FromBody] PathRule rule)
    {
        var result = await service.Add(rule);
        return new SingletonResponse<PathRule>(result);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdatePathRule")]
    public async Task<BaseResponse> Update(int id, [FromBody] PathRule rule)
    {
        rule.Id = id;
        await service.Update(rule);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeletePathRule")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("copy-config")]
    [SwaggerOperation(OperationId = "CopyPathRuleConfig")]
    public async Task<SingletonResponse<string>> CopyConfig([FromBody] string sourcePath)
    {
        var config = await service.CopyRuleConfig(sourcePath);
        return new SingletonResponse<string>(config);
    }

    [HttpPost("apply-config")]
    [SwaggerOperation(OperationId = "ApplyPathRuleConfig")]
    public async Task<BaseResponse> ApplyConfig([FromBody] ApplyPathRuleConfigInput input)
    {
        await service.ApplyRuleConfig(input.MarksJson, input.TargetPaths);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewPathRuleMatchedPaths")]
    public async Task<ListResponse<string>> PreviewMatchedPaths([FromBody] PathRule rule)
    {
        var paths = await service.PreviewMatchedPaths(rule);
        return new ListResponse<string>(paths);
    }
}

public class ApplyPathRuleConfigInput
{
    public string MarksJson { get; set; } = null!;
    public List<string> TargetPaths { get; set; } = new();
}
