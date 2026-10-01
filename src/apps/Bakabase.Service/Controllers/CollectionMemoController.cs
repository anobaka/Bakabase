using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CollectionMemo;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;
using Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[RemoteAccessible]
[Route("~/collection-memo")]
public class CollectionMemoController(CollectionMemoService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetCollectionMemoTargets")]
    public async Task<ListResponse<CollectionMemoTarget>> GetTargets() => new(await service.GetTargets());

    [HttpPost]
    [SwaggerOperation(OperationId = "CreateCollectionMemoTarget")]
    public Task<BaseResponse> CreateTarget([FromBody] CollectionMemoTargetInputModel input) => service.CreateTarget(input);

    [HttpPut("{targetId:int}")]
    [SwaggerOperation(OperationId = "UpdateCollectionMemoTarget")]
    public Task<BaseResponse> UpdateTarget(int targetId, [FromBody] CollectionMemoTargetInputModel input) =>
        service.UpdateTarget(targetId, input);

    [HttpDelete("{targetId:int}")]
    [SwaggerOperation(OperationId = "DeleteCollectionMemoTarget")]
    public Task<BaseResponse> DeleteTarget(int targetId) => service.DeleteTarget(targetId);

    [HttpPost("{targetId:int}/ranges")]
    [SwaggerOperation(OperationId = "CreateCollectionMemoRange")]
    public Task<BaseResponse> CreateRange(int targetId, [FromBody] CollectionMemoRangeInputModel input) =>
        service.CreateRange(targetId, input);

    [HttpPut("{targetId:int}/ranges/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateCollectionMemoRange")]
    public Task<BaseResponse> UpdateRange(int targetId, int id, [FromBody] CollectionMemoRangeInputModel input) =>
        service.UpdateRange(targetId, id, input);

    [HttpDelete("{targetId:int}/ranges/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteCollectionMemoRange")]
    public Task<BaseResponse> DeleteRange(int targetId, int id) => service.DeleteRange(targetId, id);
}
