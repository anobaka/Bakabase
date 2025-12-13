using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/path-rule-queue")]
public class PathRuleQueueController(IPathRuleQueueService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllPathRuleQueueItems")]
    public async Task<ListResponse<PathRuleQueueItem>> GetAll()
    {
        var items = await service.GetAll();
        return new ListResponse<PathRuleQueueItem>(items);
    }

    [HttpGet("pending")]
    [SwaggerOperation(OperationId = "GetPendingPathRuleQueueItems")]
    public async Task<ListResponse<PathRuleQueueItem>> GetPending([FromQuery] int batchSize = 100)
    {
        var items = await service.GetPendingItems(batchSize);
        return new ListResponse<PathRuleQueueItem>(items);
    }

    [HttpGet("statistics")]
    [SwaggerOperation(OperationId = "GetPathRuleQueueStatistics")]
    public async Task<SingletonResponse<QueueStatistics>> GetStatistics()
    {
        var stats = await service.GetStatistics();
        return new SingletonResponse<QueueStatistics>(stats);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "EnqueuePathRuleItem")]
    public async Task<SingletonResponse<PathRuleQueueItem>> Enqueue([FromBody] PathRuleQueueItem item)
    {
        var result = await service.Enqueue(item);
        return new SingletonResponse<PathRuleQueueItem>(result);
    }

    [HttpPatch("{id:int}/status")]
    [SwaggerOperation(OperationId = "UpdatePathRuleQueueItemStatus")]
    public async Task<BaseResponse> UpdateStatus(int id, [FromBody] UpdateStatusInput input)
    {
        await service.UpdateStatus(id, input.Status, input.Error);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("completed")]
    [SwaggerOperation(OperationId = "DeleteCompletedPathRuleQueueItems")]
    public async Task<BaseResponse> DeleteCompleted()
    {
        await service.DeleteCompletedItems();
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete]
    [SwaggerOperation(OperationId = "DeleteAllPathRuleQueueItems")]
    public async Task<BaseResponse> DeleteAll()
    {
        await service.DeleteAll();
        return BaseResponseBuilder.Ok;
    }
}

public class UpdateStatusInput
{
    public RuleQueueStatus Status { get; set; }
    public string? Error { get; set; }
}
