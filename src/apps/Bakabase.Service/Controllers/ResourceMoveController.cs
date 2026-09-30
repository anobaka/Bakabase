using Bakabase.Abstractions.Models.View;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bakabase.Modules.Federation.Identity;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource-move")]
public class ResourceMoveController(IResourceMoveService service, INodeIdentityProvider identity) : ControllerBase
{
    [HttpGet("context")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SwaggerOperation(OperationId = "GetResourceMoveContext")]
    public async Task<SingletonResponse<ResourceMoveContextViewModel>> GetContext()
    {
        var current = await identity.GetAsync();
        return new(new ResourceMoveContextViewModel(current.NodeId, current.LibraryEpoch));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "MoveResources")]
    public async Task<SingletonResponse<ResourceMoveBatchViewModel>> CreateBatch([FromBody] ResourceMoveInputModel model)
    {
        if (await ValidateSourceReferences(model) is { } error)
            return SingletonResponseBuilder<ResourceMoveBatchViewModel>.BuildBadRequest(error);
        return await service.CreateBatch(model.ResourceIds, model.DestDir, model);
    }

    [HttpGet("batches")]
    [SwaggerOperation(OperationId = "GetResourceMoveBatches")]
    public async Task<ListResponse<ResourceMoveBatchDetailViewModel>> GetBatches(string? origin = null,
        string? sourceTabId = null, bool activeOnly = false, int skip = 0, int take = 100) =>
        new(await service.GetBatches(origin, sourceTabId, activeOnly, skip, take));

    [HttpGet("batches/{batchId}")]
    [SwaggerOperation(OperationId = "GetResourceMoveBatch")]
    public async Task<SingletonResponse<ResourceMoveBatchDetailViewModel>> GetBatch(string batchId)
    {
        var batch = await service.GetBatch(batchId);
        return batch == null ? Bootstrap.Components.Miscellaneous.ResponseBuilders.SingletonResponseBuilder<ResourceMoveBatchDetailViewModel>.NotFound : new(batch);
    }

    [HttpPost("batches/{batchId}/cancel")]
    [SwaggerOperation(OperationId = "CancelResourceMoveBatch")]
    public Task<BaseResponse> CancelBatch(string batchId) => service.CancelBatch(batchId);

    [HttpPost("batches/{batchId}/retry")]
    [SwaggerOperation(OperationId = "RetryResourceMoveBatch")]
    public Task<BaseResponse> RetryBatch(string batchId) => service.RetryBatch(batchId);

    [HttpPost("records/{id:int}/resolve")]
    [SwaggerOperation(OperationId = "ResolveResourceMoveConflict")]
    public Task<BaseResponse> ResolveConflict(int id, [FromBody] ResourceMoveConflictResolution model) =>
        service.ResolveConflict(id, model);

    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewResourceMove")]
    public async Task<SingletonResponse<Bakabase.Abstractions.Models.View.ResourceMovePreviewViewModel>> Preview(
        [FromBody] ResourceMoveInputModel model)
    {
        if (await ValidateSourceReferences(model) is { } error)
            return SingletonResponseBuilder<ResourceMovePreviewViewModel>.BuildBadRequest(error);
        return await service.Preview(model.ResourceIds, model.DestDir);
    }

    private async Task<string?> ValidateSourceReferences(ResourceMoveInputModel model)
    {
        if (model.ResourceRefs == null)
            return model.Origin == "move-panel" ? "sourceContextRequired" : null;

        var references = model.ResourceRefs;
        if (model.ResourceIds == null || references.Any(r => r == null) ||
            references.Length != model.ResourceIds.Length ||
            references.Select(r => r.ResourceId).Distinct().Count() != references.Length ||
            !references.Select(r => r.ResourceId).OrderBy(id => id).SequenceEqual(model.ResourceIds.OrderBy(id => id)))
            return "invalidMoveSourceReferences";

        var current = await identity.GetAsync();
        if (references.Any(r => r.NodeId != current.NodeId)) return "foreignMoveSource";
        if (references.Any(r => r.LibraryEpoch != current.LibraryEpoch)) return "sourceContextChanged";
        // Paths from federation's playback mappings do not grant move authority. Only IDs
        // belonging to this service's current library reach its local filesystem executor.
        return null;
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
