using System.Threading.Tasks;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/post-parser/tasks")]
public sealed class PostParserBatchController(IPostParserTaskService service) : ControllerBase
{
    [HttpPost("search")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SearchPostParserTasks")]
    public async Task<SingletonResponse<PostParserTaskPage>> Search([FromBody] PostParserTaskSearchInput input,
        CancellationToken ct) => new(await service.Search(input, ct));

    [HttpGet("failed-selection")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetFailedPostParserTaskSelection")]
    public async Task<SingletonResponse<ReParseFailedPostParserTasksInput>> FailedSelection(CancellationToken ct) =>
        new(await service.GetFailedSelection(ct));

    [HttpPost("reparse-failed")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ReParseFailedPostParserTasks")]
    public async Task<SingletonResponse<PostParserReparseBatchResult>> ReParseFailed(
        [FromBody] ReParseFailedPostParserTasksInput input) => new(await service.ReParseFailed(input));

    [HttpPost("delete/preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewPostParserTaskDeletion")]
    public async Task<SingletonResponse<PostParserTaskDeletionPreview>> PreviewDeletion(
        [FromBody] PostParserTaskFilterScope input, CancellationToken ct) => new(await service.PreviewDeletion(input, ct));

    [HttpPost("delete/confirm")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ConfirmPostParserTaskDeletion")]
    public async Task<SingletonResponse<PostParserTaskDeletionResult>> ConfirmDeletion(
        [FromBody] ConfirmPostParserTaskDeletionInput input, CancellationToken ct) => new(await service.ConfirmDeletion(input, ct));
    [HttpPost("purchase/preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewPostParserTaskPurchases")]
    public async Task<SingletonResponse<PostParserTaskPurchasePreview>> PreviewPurchases(
        [FromBody] PostParserTaskFilterScope input, CancellationToken ct) => new(await service.PreviewPurchases(input, ct));

    [HttpPost("purchase/confirm")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ConfirmPostParserTaskPurchases")]
    public async Task<SingletonResponse<PostParserTaskPurchaseResult>> ConfirmPurchases(
        [FromBody] ConfirmPostParserTaskPurchasesInput input, CancellationToken ct) => new(await service.ConfirmPurchases(input, ct));

    [HttpPost("content-review/preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewPostParserContentReview")]
    public async Task<SingletonResponse<PostParserContentReviewPreview>> PreviewContentReview(
        [FromBody] PostParserContentReviewScope input, CancellationToken ct) => new(await service.PreviewContentReview(input, ct));

    [HttpPost("content-review/confirm"), RemoteAccessible, SwaggerOperation(OperationId = "ConfirmPostParserContentReview")]
    public async Task<SingletonResponse<PostParserContentReviewResult>> ConfirmContentReview(
        [FromBody] ConfirmPostParserContentReviewInput input, CancellationToken ct) => new(await service.ConfirmContentReview(input, ct));
}
