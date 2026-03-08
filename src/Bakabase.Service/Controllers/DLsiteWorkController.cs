using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/dlsite-work")]
public class DLsiteWorkController(IDLsiteWorkService service) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllDLsiteWorks")]
    public async Task<ListResponse<DLsiteWorkDbModel>> GetAll()
    {
        var data = await service.GetAll();
        return new ListResponse<DLsiteWorkDbModel>(data);
    }

    [HttpGet("{workId}")]
    [SwaggerOperation(OperationId = "GetDLsiteWorkByWorkId")]
    public async Task<SingletonResponse<DLsiteWorkDbModel>> GetByWorkId(string workId)
    {
        var data = await service.GetByWorkId(workId);
        return new SingletonResponse<DLsiteWorkDbModel>(data);
    }

    [HttpDelete("{workId}")]
    [SwaggerOperation(OperationId = "DeleteDLsiteWork")]
    public async Task<BaseResponse> Delete(string workId)
    {
        await service.DeleteByWorkId(workId);
        return BaseResponseBuilder.Ok;
    }
}
