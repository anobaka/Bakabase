using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using StackExchange.Profiling;
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
        using (MiniProfiler.Current.Step(nameof(GetAll)))
        {
            List<ResourceProfile> items;
            using (MiniProfiler.Current.Step("service.GetAll"))
            {
                items = await service.GetAll();
            }

            List<ResourceProfileViewModel> viewModels;
            using (MiniProfiler.Current.Step("ToViewModel"))
            {
                viewModels = items.Select(i => i.ToViewModel(propertyLocalizer)).ToList();
            }

            return new ListResponse<ResourceProfileViewModel>(viewModels);
        }
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

        // Normalize extensions to ensure they all start with a single dot
        model.PlayableFileOptions.NormalizeExtensions();

        var result = await service.Add(
            model.Name,
            searchJson,
            model.NameTemplate,
            model.EnhancerOptions,
            model.PlayableFileOptions,
            model.PlayerOptions,
            model.PropertyOptions,
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

        // Normalize extensions to ensure they all start with a single dot
        model.PlayableFileOptions.NormalizeExtensions();

        await service.Update(
            id,
            model.Name,
            searchJson,
            model.NameTemplate,
            model.EnhancerOptions,
            model.PlayableFileOptions,
            model.PlayerOptions,
            model.PropertyOptions,
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

    [HttpGet("by-resource/{resourceId:int}")]
    [SwaggerOperation(OperationId = "GetMatchingProfilesForResource")]
    public async Task<ListResponse<ResourceProfileViewModel>> GetMatchingProfilesForResource(int resourceId, [FromServices] IResourceService resourceService)
    {
        var resource = await resourceService.Get(resourceId, ResourceAdditionalItem.None);
        if (resource == null)
        {
            return new ListResponse<ResourceProfileViewModel>();
        }
        var profiles = await service.GetMatchingProfiles(resource);
        return new ListResponse<ResourceProfileViewModel>(profiles.Select(p => p.ToViewModel(propertyLocalizer)).ToList());
    }

    // Note: For testing search criteria, use ResourceController.Search directly
    // with the same search parameters (group, keyword, tags)
}
