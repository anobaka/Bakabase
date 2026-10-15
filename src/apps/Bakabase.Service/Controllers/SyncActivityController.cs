using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.DataSync;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Components.Federation.Processing;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Ordinary administration reads, with the same gate as /data-sync. No remote
/// machine action, grant widening, remote probes, or resource payloads.</summary>
[ApiController]
[Route("data-sync/activity")]
public sealed class SyncActivityController(SyncActivityService activity, MetadataSyncActivityStore history,
    ResourceProcessingJobStore processing) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetSyncActivity")]
    public Task<SyncActivityPage> Get([FromQuery] string? dataType = null, [FromQuery] string? deviceId = null,
        [FromQuery] string? state = null, [FromQuery] int offset = 0, [FromQuery] int take = 50,
        CancellationToken ct = default) => activity.GetAsync(dataType, deviceId, state, offset, take, ct);

    [HttpGet("metadata-history")]
    [SwaggerOperation(OperationId = "GetMetadataSyncHistory")]
    public Task<MetadataSyncActivityPage> History([FromQuery] string? serverId = null,
        [FromQuery] long? beforeId = null, [FromQuery] int take = 30, CancellationToken ct = default) =>
        history.ReadAsync(serverId, beforeId, take, ct);

    [HttpGet("processing-history")]
    [SwaggerOperation(OperationId = "GetSyncProcessingHistory")]
    public Task<IReadOnlyList<ResourceProcessingJobView>> Processing([FromQuery] ResourceProcessingRole role = ResourceProcessingRole.Manager,
        [FromQuery] string? beforeId = null, [FromQuery] int take = 30, CancellationToken ct = default) =>
        processing.ListAsync(Enum.IsDefined(role) ? role : ResourceProcessingRole.Manager, beforeId, Math.Clamp(take, 1, 100), ct);
}
