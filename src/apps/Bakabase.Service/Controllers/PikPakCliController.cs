using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/pikpak-cli")]
[RemoteAccessible]
[OwnerUiOrigin("PIKPAK_BROWSER_ORIGIN_REJECTED")]
[PikPakCliErrors]
public sealed class PikPakCliController(PikPakCliAccountService accounts) : Controller
{
    [HttpGet("account")]
    [SwaggerOperation(OperationId = "GetPikPakCliAccount")]
    public async Task<SingletonResponse<PikPakCliAccountViewModel>> Get(CancellationToken ct) => new(await accounts.GetAsync(ct));
    [HttpGet("account/authorization")]
    [SwaggerOperation(OperationId = "GetActivePikPakCliAuthorization")]
    public async Task<SingletonResponse<PikPakCliAuthorizationViewModel?>> GetActiveAuthorization(CancellationToken ct) =>
        new(await accounts.GetActiveAuthorizationAsync(ct));
    [HttpPost("account/authorization")]
    [SwaggerOperation(OperationId = "StartPikPakCliAuthorization")]
    public async Task<SingletonResponse<PikPakCliAuthorizationViewModel>> Start(CancellationToken ct) => new(await accounts.StartAsync(ct));
    [HttpPost("account/authorization/{id}/poll")]
    [SwaggerOperation(OperationId = "PollPikPakCliAuthorization")]
    public async Task<SingletonResponse<PikPakCliAuthorizationViewModel>> Poll(string id, CancellationToken ct) => new(await accounts.PollAsync(id, ct));
    [HttpDelete("account/authorization/{id}")]
    [SwaggerOperation(OperationId = "CancelPikPakCliAuthorization")]
    public async Task<BaseResponse> Cancel(string id, CancellationToken ct) { await accounts.CancelAsync(id, ct); return BaseResponseBuilder.Ok; }
    [HttpDelete("account")]
    [SwaggerOperation(OperationId = "LogoutPikPakCliAccount")]
    public async Task<BaseResponse> Logout(CancellationToken ct) { await accounts.LogoutAsync(ct); return BaseResponseBuilder.Ok; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class PikPakCliErrorsAttribute : Attribute, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PikPakCliException error) return;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(BaseResponseBuilder.BuildBadRequest(error.Code));
        context.ExceptionHandled = true;
    }
}
