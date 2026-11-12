using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads.TeraBox;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/terabox")]
[RemoteAccessible]
[OwnerUiOrigin("TERABOX_BROWSER_ORIGIN_REJECTED")]
[TeraBoxErrors]
public sealed class TeraBoxController(TeraBoxAccountService accounts) : Controller
{
    [HttpGet("account")]
    [SwaggerOperation(OperationId = "GetTeraBoxAccount")]
    public async Task<SingletonResponse<TeraBoxAccountViewModel>> Get(CancellationToken ct) => new(await accounts.GetAsync(ct));
    [HttpGet("account/authorization")]
    [SwaggerOperation(OperationId = "GetActiveTeraBoxAuthorization")]
    public async Task<SingletonResponse<TeraBoxAuthorizationViewModel?>> GetActive(CancellationToken ct) => new(await accounts.GetActiveAuthorizationAsync(ct));
    [HttpPost("account/authorization")]
    [SwaggerOperation(OperationId = "StartTeraBoxAuthorization")]
    public async Task<SingletonResponse<TeraBoxAuthorizationViewModel>> Start(CancellationToken ct) => new(await accounts.StartAsync(ct));
    [HttpPost("account/authorization/{id}/complete")]
    [SwaggerOperation(OperationId = "CompleteTeraBoxAuthorization")]
    public async Task<SingletonResponse<TeraBoxAuthorizationViewModel>> Complete(string id, [FromBody] TeraBoxAuthorizationCompleteInput input, CancellationToken ct) =>
        new(await accounts.CompleteAsync(id, input.Code, ct));
    [HttpDelete("account/authorization/{id}")]
    [SwaggerOperation(OperationId = "CancelTeraBoxAuthorization")]
    public async Task<BaseResponse> Cancel(string id, CancellationToken ct) { await accounts.CancelAsync(id, ct); return BaseResponseBuilder.Ok; }
    [HttpDelete("account")]
    [SwaggerOperation(OperationId = "LogoutTeraBoxAccount")]
    public async Task<BaseResponse> Logout(CancellationToken ct) { await accounts.LogoutAsync(ct); return BaseResponseBuilder.Ok; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class TeraBoxErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TeraBoxException error) return;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(BaseResponseBuilder.BuildBadRequest(error.Code));
        context.ExceptionHandled = true;
    }
}
