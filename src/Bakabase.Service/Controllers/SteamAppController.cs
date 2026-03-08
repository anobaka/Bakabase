using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/steam-app")]
public class SteamAppController(ISteamAppService service) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllSteamApps")]
    public async Task<ListResponse<SteamAppDbModel>> GetAll()
    {
        var data = await service.GetAll();
        return new ListResponse<SteamAppDbModel>(data);
    }

    [HttpGet("{appId:int}")]
    [SwaggerOperation(OperationId = "GetSteamAppByAppId")]
    public async Task<SingletonResponse<SteamAppDbModel>> GetByAppId(int appId)
    {
        var data = await service.GetByAppId(appId);
        return new SingletonResponse<SteamAppDbModel>(data);
    }

    [HttpDelete("{appId:int}")]
    [SwaggerOperation(OperationId = "DeleteSteamApp")]
    public async Task<BaseResponse> Delete(int appId)
    {
        await service.DeleteByAppId(appId);
        return BaseResponseBuilder.Ok;
    }
}
