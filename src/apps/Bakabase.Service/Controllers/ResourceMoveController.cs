using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource-move")]
public class ResourceMoveController(IResourceMoveService service) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(OperationId = "MoveResources")]
    public async Task<SingletonResponse<string>> CreateBatch([FromBody] ResourceMoveInputModel model)
    {
        return await service.CreateBatch(model.ResourceIds, model.DestDir);
    }

    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewResourceMove")]
    public async Task<SingletonResponse<Bakabase.Abstractions.Models.View.ResourceMovePreviewViewModel>> Preview(
        [FromBody] ResourceMoveInputModel model)
    {
        return await service.Preview(model.ResourceIds, model.DestDir);
    }

    [HttpGet("records")]
    [SwaggerOperation(OperationId = "GetResourceMoveRecords")]
    public async Task<ListResponse<ResourceMoveRecordDbModel>> GetRecords([FromQuery] int maxCount = 100)
    {
        var records = await service.GetRecords(maxCount);
        return new ListResponse<ResourceMoveRecordDbModel>(records);
    }

    [HttpPost("records/{id:int}/retry")]
    [SwaggerOperation(OperationId = "RetryResourceMoveRecord")]
    public async Task<BaseResponse> Retry(int id)
    {
        return await service.Retry(id);
    }

    [HttpDelete("records/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteResourceMoveRecord")]
    public async Task<BaseResponse> DeleteRecord(int id)
    {
        return await service.DeleteRecord(id);
    }

    [HttpDelete("records/inactive")]
    [SwaggerOperation(OperationId = "DeleteInactiveResourceMoveRecords")]
    public async Task<BaseResponse> DeleteInactiveRecords()
    {
        return await service.DeleteInactiveRecords();
    }
}
