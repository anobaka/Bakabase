using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.ResourceMove;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource-move/panel-options")]
public class ResourceMovePanelController(ResourceMovePanelSettings settings, IResourceMoveService moveService) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetResourceMovePanelOptions")]
    public SingletonResponse<ResourceMovePanelOptions> Get() => new(settings.Get());

    [HttpPut]
    [SwaggerOperation(OperationId = "SaveResourceMovePanelOptions")]
    public async Task<SingletonResponse<ResourceMovePanelOptions>> Put([FromBody] ResourceMovePanelOptions input)
    {
        try
        {
            if (!await settings.Save(input))
                return SingletonResponseBuilder<ResourceMovePanelOptions>.Build(ResponseCode.Conflict,
                    "Move panel settings changed. Reload and try again.");
            await moveService.ApplyPanelPolicy();
            return new SingletonResponse<ResourceMovePanelOptions>(settings.Get());
        }
        catch (ArgumentException e)
        {
            return SingletonResponseBuilder<ResourceMovePanelOptions>.Build(ResponseCode.InvalidPayloadOrOperation,
                e.Message);
        }
    }
}
