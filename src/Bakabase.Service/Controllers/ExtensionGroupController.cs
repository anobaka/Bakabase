using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/extension-group")]
public class ExtensionGroupController(IExtensionGroupService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllExtensionGroups")]
    public async Task<ListResponse<ExtensionGroup>> GetAll()
    {
        var groups = await service.GetAll();
        return new ListResponse<ExtensionGroup>(groups);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetExtensionGroup")]
    public async Task<SingletonResponse<ExtensionGroup>> Get(int id)
    {
        var g = await service.Get(id);
        return new SingletonResponse<ExtensionGroup>(g);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddExtensionGroup")]
    public async Task<SingletonResponse<ExtensionGroup>> Add([FromBody] ExtensionGroupAddInputModel group)
    {
        return new SingletonResponse<ExtensionGroup>(await service.Add(group));
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "PutExtensionGroup")]
    public async Task<BaseResponse> Put(int id, [FromBody] ExtensionGroupPutInputModel group)
    {
        await service.Put(id, group);
        return BaseResponseBuilder.Ok;

    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteExtensionGroup")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }
}