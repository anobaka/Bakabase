using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource-profile")]
public class ResourceProfileController(IResourceProfileService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllResourceProfiles")]
    public async Task<ListResponse<ResourceProfile>> GetAll()
    {
        var items = await service.GetAll();
        return new ListResponse<ResourceProfile>(items);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetResourceProfile")]
    public async Task<SingletonResponse<ResourceProfile?>> Get(int id)
    {
        var item = await service.Get(id);
        return new SingletonResponse<ResourceProfile?>(item);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddResourceProfile")]
    public async Task<SingletonResponse<ResourceProfile>> Add([FromBody] ResourceProfile profile)
    {
        var result = await service.Add(profile);
        return new SingletonResponse<ResourceProfile>(result);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdateResourceProfile")]
    public async Task<BaseResponse> Update(int id, [FromBody] ResourceProfile profile)
    {
        profile.Id = id;
        await service.Update(profile);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteResourceProfile")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    // Note: For testing search criteria, use ResourceController.Search directly
    // with the same search parameters (group, keyword, tags)
}
