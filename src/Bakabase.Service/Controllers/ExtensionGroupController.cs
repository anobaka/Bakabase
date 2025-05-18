using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/extension-group")]
public class ExtensionGroupController(IExtensionGroupService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ExtensionGroup[]>> GetAll()
    {
        var groups = await service.GetAll();
        return Ok(groups);
    }

    [HttpGet("{id:int}")]
    public async Task<SingletonResponse<ExtensionGroup>> Get(int id)
    {
        var g = await service.Get(id);
        return new SingletonResponse<ExtensionGroup>(g);
    }

    [HttpPost]
    public async Task<BaseResponse> Add([FromBody] ExtensionGroup group)
    {
        await service.Add(group);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{id:int}")]
    public async Task<BaseResponse> Put(int id, [FromBody] ExtensionGroup group)
    {
        await service.Put(id, group);
        return BaseResponseBuilder.Ok;

    }

    [HttpDelete("{id:int}")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }
}