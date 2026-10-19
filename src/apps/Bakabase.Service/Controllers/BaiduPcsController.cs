using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/baidu-pcs/accounts")]
public sealed class BaiduPcsController(BaiduPcsAccountStore accounts) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetBaiduPcsAccounts")]
    public async Task<ListResponse<BaiduPcsAccountViewModel>> List(CancellationToken ct) =>
        new(await accounts.ListAsync(ct));

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "LoginBaiduPcsAccount")]
    public async Task<SingletonResponse<BaiduPcsAccountViewModel>> Login([FromBody] BaiduPcsLoginInputModel input, CancellationToken ct) =>
        new(await accounts.LoginAsync(input, ct));

    [HttpPost("{id}/validate")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ValidateBaiduPcsAccount")]
    public async Task<SingletonResponse<BaiduPcsAccountViewModel>> Validate(string id, CancellationToken ct) =>
        new(await accounts.ValidateAsync(id, ct));

    [HttpPut("{id}/default")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SetDefaultBaiduPcsAccount")]
    public async Task<BaseResponse> SetDefault(string id, CancellationToken ct)
    { await accounts.SetDefaultAsync(id, ct); return new BaseResponse(); }

    [HttpPost("{id}/logout")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "LogoutBaiduPcsAccount")]
    public async Task<BaseResponse> Logout(string id, CancellationToken ct)
    { await accounts.LogoutAsync(id, false, ct); return new BaseResponse(); }

    [HttpDelete("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "DeleteBaiduPcsAccount")]
    public async Task<BaseResponse> Delete(string id, CancellationToken ct)
    { await accounts.LogoutAsync(id, true, ct); return new BaseResponse(); }
}
