using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Server administration of private rclone accounts. Credentials are write-only.</summary>
[Route("~/rclone/accounts")]
[RemoteAccessible(false)]
public sealed class RcloneAccountsController(RcloneAccountStore accounts, RcloneAuthorizationService authorization) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetRcloneAccounts")]
    public async Task<ListResponse<RcloneAccountViewModel>> List([FromQuery] string? providerId, CancellationToken ct) =>
        new((await accounts.ListAsync(providerId, ct)).ToList());

    [HttpPost]
    [SwaggerOperation(OperationId = "SaveRcloneAccount")]
    public async Task<SingletonResponse<RcloneAccountViewModel>> Save([FromBody] RcloneAccountInputModel input, CancellationToken ct) =>
        new(await accounts.SaveAsync(input, ct));

    [HttpPut("{id}/default")]
    [SwaggerOperation(OperationId = "SetDefaultRcloneAccount")]
    public async Task<BaseResponse> SetDefault(string id, CancellationToken ct)
    { await accounts.SetDefaultAsync(id, ct); return BaseResponseBuilder.Ok; }

    [HttpPost("{id}/validate")]
    [SwaggerOperation(OperationId = "ValidateRcloneAccount")]
    public async Task<SingletonResponse<RcloneAccountViewModel>> Validate(string id, CancellationToken ct) =>
        new(await accounts.ValidateAsync(id, ct));

    [HttpDelete("{id}")]
    [SwaggerOperation(OperationId = "DeleteRcloneAccount")]
    public async Task<BaseResponse> Delete(string id, CancellationToken ct)
    { await authorization.CancelForAccountAsync(id, ct); await accounts.DeleteAsync(id, ct); return BaseResponseBuilder.Ok; }

    [HttpPost("{id}/authorization")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "StartRcloneAuthorization")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> Start(string id, CancellationToken ct) =>
        new(await authorization.StartAsync(id, ct));

    [HttpGet("authorization/{sessionId}")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "GetRcloneAuthorization")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> Get(string sessionId, CancellationToken ct) =>
        new(await authorization.GetAsync(sessionId, ct));

    [HttpGet("authorization/{sessionId}/drives")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "GetRcloneAuthorizationDrives")]
    public async Task<SingletonResponse<RcloneDriveChoicesViewModel>> GetDrives(string sessionId, [FromQuery] string questionId, CancellationToken ct) =>
        new(await authorization.GetDrivesAsync(sessionId, questionId, ct));

    [HttpPost("authorization/{sessionId}/drives/preview")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "PreviewRcloneAuthorizationDrive")]
    public async Task<SingletonResponse<RcloneDrivePreviewViewModel>> PreviewDrive(string sessionId,
        [FromBody] RcloneDrivePreviewInputModel input, CancellationToken ct) =>
        new(await authorization.PreviewDriveAsync(sessionId, input, ct));

    [HttpPost("authorization/{sessionId}/answer")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "AnswerRcloneAuthorization")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> Answer(string sessionId,
        [FromBody] RcloneAuthorizationAnswerInputModel input, CancellationToken ct) =>
        new(await authorization.AnswerAsync(sessionId, input, ct));

    [HttpDelete("authorization/{sessionId}")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "CancelRcloneAuthorization")]
    public async Task<BaseResponse> Cancel(string sessionId, CancellationToken ct)
    { await authorization.CancelAsync(sessionId, ct); return BaseResponseBuilder.Ok; }

    [HttpPost("authorization/{sessionId}/browser/start")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "StartRcloneBrowserAuthorization")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> StartBrowser(string sessionId,
        [FromBody] RcloneBrowserAuthorizationStartInputModel input, CancellationToken ct) =>
        new(await authorization.StartBrowserAsync(sessionId, input, ct));

    [HttpPost("authorization/{sessionId}/browser/callback")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "SubmitRcloneBrowserAuthorizationCallback")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> SubmitBrowserCallback(string sessionId,
        [FromBody] RcloneBrowserAuthorizationCallbackInputModel input, CancellationToken ct) =>
        new(await authorization.SubmitBrowserCallbackAsync(sessionId, input, ct));

    [HttpDelete("authorization/{sessionId}/browser")]
    [RcloneBrowserOrigin]
    [RcloneAuthorizationErrors]
    [SwaggerOperation(OperationId = "StopRcloneBrowserAuthorization")]
    public async Task<SingletonResponse<RcloneAuthorizationSessionViewModel>> StopBrowser(string sessionId, CancellationToken ct) =>
        new(await authorization.StopBrowserAsync(sessionId, ct));
}
