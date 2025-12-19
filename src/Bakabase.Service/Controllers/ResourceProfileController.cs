using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource-profile")]
public class ResourceProfileController(IResourceProfileService service, IPropertyLocalizer propertyLocalizer) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllResourceProfiles")]
    public async Task<ListResponse<ResourceProfileViewModel>> GetAll()
    {
        var items = await service.GetAll();
        return new ListResponse<ResourceProfileViewModel>(items.Select(i => i.ToViewModel(propertyLocalizer)).ToList());
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetResourceProfile")]
    public async Task<SingletonResponse<ResourceProfileViewModel?>> Get(int id)
    {
        var item = await service.Get(id);
        return new SingletonResponse<ResourceProfileViewModel?>(item?.ToViewModel(propertyLocalizer));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddResourceProfile")]
    public async Task<SingletonResponse<ResourceProfileViewModel>> Add([FromBody] ResourceProfileInputModel model)
    {
        var searchJson = model.Search != null
            ? JsonConvert.SerializeObject(model.Search.ToDbModel())
            : null;

        var result = await service.Add(
            model.Name,
            searchJson,
            model.NameTemplate,
            model.EnhancerOptions,
            model.PlayableFileOptions,
            model.PlayerOptions,
            model.Priority);

        return new SingletonResponse<ResourceProfileViewModel>(result.ToViewModel(propertyLocalizer));
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdateResourceProfile")]
    public async Task<BaseResponse> Update(int id, [FromBody] ResourceProfileInputModel model)
    {
        var searchJson = model.Search != null
            ? JsonConvert.SerializeObject(model.Search.ToDbModel())
            : null;

        await service.Update(
            id,
            model.Name,
            searchJson,
            model.NameTemplate,
            model.EnhancerOptions,
            model.PlayableFileOptions,
            model.PlayerOptions,
            model.Priority);

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
