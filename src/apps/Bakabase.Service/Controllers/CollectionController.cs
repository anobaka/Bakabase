using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Domain;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/collection")]
public class CollectionController(
    ICollectionService service,
    IResourceService resources,
    IPlaceholderResourceService placeholders) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllCollections")]
    public async Task<ListResponse<ResourceCollection>> GetAll([FromQuery] bool withProgress = true) =>
        new(await service.GetAll(withProgress));

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetCollection")]
    public async Task<SingletonResponse<ResourceCollection?>> Get(int id) =>
        new(await service.Get(id, true));

    [HttpPost]
    [SwaggerOperation(OperationId = "AddCollection")]
    public async Task<SingletonResponse<ResourceCollection>> Add([FromBody] CollectionInputModel model) =>
        new(await service.Add(model));

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "PutCollection")]
    public async Task<SingletonResponse<ResourceCollection>> Put(int id,
        [FromBody] CollectionInputModel model)
    {
        try
        {
            return new SingletonResponse<ResourceCollection>(await service.Put(id, model));
        }
        catch (InvalidOperationException e)
        {
            return SingletonResponseBuilder<ResourceCollection>.BuildBadRequest(e.Message);
        }
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteCollection")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);

        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// One page of members. Ids only: the resource page's own endpoints load them with whatever
    /// extras the caller wants, and duplicating that here would be a second way to load a resource.
    /// </summary>
    [HttpGet("{id:int}/members")]
    [SwaggerOperation(OperationId = "SearchCollectionMembers")]
    public async Task<SingletonResponse<CollectionMemberPage>> SearchMembers(int id,
        [FromQuery] CollectionMemberFilter filter = CollectionMemberFilter.All,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 60) =>
        new(await service.SearchMembers(id, filter, pageIndex, pageSize));

    /// <summary>Every membership with its state, for the grid's chips and right-click actions.</summary>
    [HttpGet("{id:int}/memberships")]
    [SwaggerOperation(OperationId = "GetCollectionMemberships")]
    public async Task<ListResponse<CollectionMember>> GetMemberships(int id) =>
        new(await service.GetMembers(id));

    [HttpPost("{id:int}/members")]
    [SwaggerOperation(OperationId = "AddCollectionMembers")]
    public async Task<BaseResponse> AddMembers(int id, [FromBody] CollectionMembersInputModel model)
    {
        await service.AddMembers(id, model.ResourceIds);

        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}/members")]
    [SwaggerOperation(OperationId = "RemoveCollectionMembers")]
    public async Task<BaseResponse> RemoveMembers(int id, [FromBody] CollectionMembersInputModel model)
    {
        await service.RemoveMembers(id, model.ResourceIds);

        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Not wanted, but not forgotten. An ignored member leaves the collected ratio entirely, which
    /// is what keeps the number honest when a series includes three things nobody wants.
    /// </summary>
    [HttpPut("{id:int}/members/{resourceId:int}/ignored")]
    [SwaggerOperation(OperationId = "SetCollectionMemberIgnored")]
    public async Task<BaseResponse> SetIgnored(int id, int resourceId, [FromQuery] bool ignored)
    {
        await service.SetMemberIgnored(id, resourceId, ignored);

        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{id:int}/members/order")]
    [SwaggerOperation(OperationId = "ReorderCollectionMembers")]
    public async Task<BaseResponse> Reorder(int id, [FromBody] CollectionMembersInputModel model)
    {
        await service.ReorderMembers(id, model.ResourceIds);

        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// "I know this exists and I do not have it." Creating the resource and adding it are one
    /// gesture, because saying it about a collection is the commonest way of saying it at all.
    /// </summary>
    [HttpPost("{id:int}/members/placeholder")]
    [SwaggerOperation(OperationId = "AddCollectionPlaceholderMember")]
    public async Task<SingletonResponse<PlaceholderResourceResult>> AddPlaceholder(int id,
        [FromBody] CollectionPlaceholderInputModel model)
    {
        var result = await placeholders.CreateByTitle(model.Title);

        await service.AddMembers(id, [result.ResourceId]);

        return new SingletonResponse<PlaceholderResourceResult>(result);
    }

    [HttpGet("{id:int}/progress")]
    [SwaggerOperation(OperationId = "GetCollectionProgress")]
    public async Task<SingletonResponse<CollectionProgress>> GetProgress(int id) =>
        new(await service.GetProgress(id));

    /// <summary>Which collections these resources are in — one call for a whole page of cards.</summary>
    [HttpGet("~/resource/collections")]
    [SwaggerOperation(OperationId = "GetCollectionIdsByResourceIds")]
    public async Task<SingletonResponse<Dictionary<int, List<int>>>> GetByResourceIds(
        [FromQuery] int[] resourceIds, [FromServices] ICollectionResourceMappingService mappings) =>
        new(await mappings.GetCollectionIdsByResourceIds(resourceIds));
}

public record CollectionPlaceholderInputModel
{
    public string Title { get; set; } = null!;
}
