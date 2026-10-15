using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Service.Components.Federation.Processing;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Local manager controls. Ordinary administrator access; no read-only federation route.</summary>
[ApiController]
[Route("resource-processing/jobs")]
public sealed class ResourceProcessingController(ResourceProcessingCoordinator coordinator,
    ResourceProcessingJobStore store) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetResourceProcessingJobs")]
    public Task<IReadOnlyList<ResourceProcessingJobView>> Get([FromQuery] string? beforeId = null,
        [FromQuery] int take = 30, CancellationToken ct = default) => store.ListAsync(ResourceProcessingRole.Manager, beforeId, Math.Clamp(take, 1, 100), ct);

    [HttpPost]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "StartResourceProcessing")]
    [ProducesResponseType(typeof(ResourceProcessingJobView), 200)]
    public Task<IActionResult> Start([FromBody] ResourceProcessingStart input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await coordinator.StartAsync(input, ct));

    [HttpPost("{jobId}/control")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "ControlResourceProcessing")]
    [ProducesResponseType(typeof(ResourceProcessingJobView), 200)]
    public Task<IActionResult> Control(string jobId, [FromBody] ResourceProcessingControl input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await coordinator.ControlAsync(jobId, input, ct));
}

/// <summary>Paired-device work protocol. Even unrestricted anonymous browsers cannot submit
/// prepared work or collect another requester's results; loopback is the local coordinator.</summary>
[ApiController]
[Route("management/resource-processing")]
[RequestSizeLimit(ResourceProcessingWire.MaxJsonBytes)]
public sealed class ManagedResourceProcessingController(ResourceProcessingOwner owner,
    ResourceProcessingJobStore store, ResourceProcessingArtifacts artifacts, INodeIdentityProvider identity) : ControllerBase
{
    private async Task<string> Requester(CancellationToken ct)
    {
        var context = HttpContext.GetRemoteAccessContext();
        if (context is {IsPaired: true, Device: not null}) return context.Device.Id;
        if (context is {IsLoopback: true}) return ResourceProcessingCoordinator.LocalRequester((await identity.GetAsync(ct)).NodeId);
        throw new FederationAccessException("ManagementPairingRequired", 403, "Processing requires an explicitly paired manager.");
    }

    [HttpPost("owner/prepare")]
    [SwaggerOperation(OperationId = "PrepareManagedResourceProcessing")]
    [ProducesResponseType(typeof(ResourceProcessingPrepared), 200)]
    public Task<IActionResult> Prepare([FromBody] ResourceProcessingInput input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await owner.PrepareAsync(await Requester(ct), input, ct));

    [HttpPost("owner/{jobId}/apply")]
    [SwaggerOperation(OperationId = "ApplyManagedResourceProcessing")]
    [ProducesResponseType(typeof(ResourceProcessingReceipt), 200)]
    public Task<IActionResult> Apply(string jobId, [FromBody] ResourceProcessingApply input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await owner.ApplyAsync(await Requester(ct), jobId, input.Token, input.Output, ct));

    [HttpPost("owner/{jobId}/receipt")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "GetManagedResourceProcessingReceipt")]
    [ProducesResponseType(typeof(ResourceProcessingReceiptStatus), 200)]
    public Task<IActionResult> Receipt(string jobId, [FromBody] ResourceProcessingReceiptRequest input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => new ResourceProcessingReceiptStatus(
            await owner.GetReceiptAsync(await Requester(ct), jobId, input.Token, ct)));

    [HttpPost("owner/artifact")]
    [SwaggerOperation(OperationId = "UploadManagedResourceProcessingArtifact")]
    [ProducesResponseType(typeof(long), 200)]
    public Task<IActionResult> Upload([FromBody] ResourceProcessingArtifactChunk input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await owner.PutArtifactChunkAsync(await Requester(ct), input, ct));

    [HttpPost("owner/{jobId}/abandon")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "AbandonManagedResourceProcessing")]
    [ProducesResponseType(typeof(bool), 200)]
    public Task<IActionResult> Abandon(string jobId, [FromBody] ResourceProcessingAbandonRequest input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () => await owner.AbandonAsync(await Requester(ct), jobId, input.Token, ct));

    [HttpPost("worker/queue")]
    [SwaggerOperation(OperationId = "QueueManagedResourceProcessing")]
    [ProducesResponseType(typeof(ResourceProcessingWorkerResult), 200)]
    public Task<IActionResult> Queue([FromBody] ResourceProcessingPrepared input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () =>
        {
            var requester = await Requester(ct);
            ResourceProcessingWire.ValidateInput(input.Input);
            if (!ResourceProcessingWire.IsHex(input.Token) || input.Token.Length != 64 ||
                string.IsNullOrEmpty(input.ResourceJson) || string.IsNullOrEmpty(input.CatalogGeneration))
                throw new ResourceProcessingException("InvalidInput", "An owner preparation is required.");
            var self = await identity.GetAsync(ct);
            var expectedEpoch = Request.Query["managedLibraryEpoch"].ToString();
            if (expectedEpoch.Length > 0 && expectedEpoch != self.LibraryEpoch)
                throw new ResourceProcessingException("EpochChanged", "The worker's data directory was replaced.");
            var json = ResourceProcessingWire.Serialize(new ResourceProcessingWorkerEnvelope(input, self.NodeId, self.LibraryEpoch));
            ResourceProcessingJobDbModel row;
            try { row = await store.CreateAsync(ResourceProcessingRole.Worker, requester, input.Input, json, ct); }
            catch (ResourceProcessingException error) when (error.Code == "PendingJobLimit")
            {
                // Only inspect retained payloads when admission is exhausted. Completed
                // but unacknowledged work from a replaced library can no longer replay.
                foreach (var expired in await store.GetRetainedWorkersAsync(requester, ct))
                    await ReleaseExpiredWorker(expired, self, ct);
                row = await store.CreateAsync(ResourceProcessingRole.Worker, requester, input.Input, json, ct);
            }
            if (await ReleaseExpiredWorker(row, self, ct))
                throw new ResourceProcessingException("EpochChanged", "This work belongs to the worker's previous data directory.");
            ResourceProcessingWorkerEnvelope.Read(row.InputJson).RequireIdentity(await identity.GetAsync(ct));
            if (row.InputJson != json)
                throw new ResourceProcessingException("Conflict", "This work id already contains a different prepared resource.");
            return new ResourceProcessingWorkerResult(ResourceProcessingCoordinator.View(row), row.State == "Completed" && row.ResultJson != null
                ? ResourceProcessingWire.Deserialize<ResourceProcessingOutput>(row.ResultJson) : null);
        });

    [HttpPost("worker/{jobId}/artifact")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "ReadManagedResourceProcessingArtifact")]
    [ProducesResponseType(typeof(ResourceProcessingArtifactChunk), 200)]
    public Task<IActionResult> Artifact(string jobId, [FromBody] ResourceProcessingArtifactRead input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () =>
        {
            var row = await store.GetAsync(ResourceProcessingRole.Worker, await Requester(ct), jobId, ct)
                ?? throw new ResourceProcessingException("JobMissing", "This work result does not exist.");
            if (row.State != "Completed" || row.ResultJson == null)
                throw new ResourceProcessingException("InvalidState", "Wait for the work result before requesting its files.");
            var result = ResourceProcessingWire.Deserialize<ResourceProcessingOutput>(row.ResultJson);
            var artifact = result.Artifacts.SingleOrDefault(a => a.Hash == input.Hash)
                ?? throw new ResourceProcessingException("InvalidArtifact", "This file is not part of the work result.");
            if (input.Offset < 0 || input.Offset > artifact.Length)
                throw new ResourceProcessingException("InvalidArtifact", "The file offset is invalid.");
            var saved = ResourceProcessingWorkerEnvelope.Read(row.InputJson);
            saved.RequireIdentity(await identity.GetAsync(ct));
            var prepared = saved.Prepared;
            await using var stream = artifacts.OpenWorkerArtifact(prepared, input.Hash);
            stream.Position = input.Offset;
            var bytes = new byte[(int)Math.Min(ResourceProcessingWire.ArtifactChunkBytes, artifact.Length - input.Offset)];
            await stream.ReadExactlyAsync(bytes, ct);
            return new ResourceProcessingArtifactChunk(jobId, prepared.Token, artifact, input.Offset, Convert.ToBase64String(bytes));
        });

    [HttpPost("worker/{jobId}/ack")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "AcknowledgeManagedResourceProcessing")]
    [ProducesResponseType(typeof(bool), 200)]
    public Task<IActionResult> Acknowledge(string jobId, CancellationToken ct) => ResourceProcessingEndpoint.Run(this, async () =>
    {
        var row = await store.GetAsync(ResourceProcessingRole.Worker, await Requester(ct), jobId, ct)
            ?? throw new ResourceProcessingException("JobMissing", "This work result does not exist.");
        if (row.State != "Completed") throw new ResourceProcessingException("InvalidState", "Only completed work can be acknowledged.");
        if (row.ResultJson == null) return true;
        var prepared = ResourceProcessingWorkerEnvelope.Read(row.InputJson).Prepared;
        artifacts.Cleanup(prepared, "worker");
        // Keep identity/idempotence summary, not complete resource metadata or result values.
        row.InputJson = "{}"; row.ResultJson = null;
        await store.UpdateAsync(row, ct);
        return true;
    });

    [HttpPost("worker/{jobId}/control")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "ControlManagedResourceProcessingWorker")]
    [ProducesResponseType(typeof(bool), 200)]
    public Task<IActionResult> Control(string jobId, [FromBody] ResourceProcessingWorkerControl input, CancellationToken ct) =>
        ResourceProcessingEndpoint.Run(this, async () =>
        {
            var row = await store.GetAsync(ResourceProcessingRole.Worker, await Requester(ct), jobId, ct)
                ?? throw new ResourceProcessingException("JobMissing", "This work result does not exist.");
            if (row.State is "Completed" or "Cancelled") return true;
            if (input.Action is "resume" or "retry")
                ResourceProcessingWorkerEnvelope.Read(row.InputJson).RequireIdentity(await identity.GetAsync(ct));
            switch (input.Action)
            {
                case "pause": row.PauseRequested = true; if (row.State == "Queued") row.State = "Paused"; break;
                case "cancel": row.CancelRequested = true; if (row.State != "Running") row.State = "Cancelled"; break;
                case "resume": row.PauseRequested = false; if (row.State == "Paused") row.State = "Queued"; break;
                case "retry" when row.State == "Failed":
                    row.State = "Queued"; row.PauseRequested = false; row.Error = null; row.ErrorCode = null; break;
                case "retry" when row.State is "Queued" or "Running": return true;
                default: throw new ResourceProcessingException("InvalidState", "Choose pause, resume, cancel, or retry a failed job.");
            }
            await store.UpdateAsync(row, ct);
            return true;
        });

    [HttpPost("worker/{jobId}/abandon")]
    [RequestSizeLimit(4096)]
    [SwaggerOperation(OperationId = "AbandonManagedResourceProcessingWorker")]
    [ProducesResponseType(typeof(bool), 200)]
    public Task<IActionResult> AbandonWorker(string jobId, CancellationToken ct) => ResourceProcessingEndpoint.Run(this, async () =>
    {
        var requester = await Requester(ct);
        var row = await store.GetAsync(ResourceProcessingRole.Worker, requester, jobId, ct)
            ?? await store.AbandonMissingAsync(ResourceProcessingRole.Worker, requester, jobId, ct);
        if (row.InputJson == "{}") return true;
        row.CancelRequested = true;
        if (row.State == "Running")
        {
            // The next worker checkpoint acknowledges cancellation. Never delete a file
            // that an active enhancer may still be writing into this job's directory.
            await store.UpdateAsync(row, ct);
            return false;
        }
        // Claim the stopped state before removing files; a concurrent queue runner must
        // lose its CAS before it can start computation using these files.
        row.State = "Cancelled";
        await store.UpdateAsync(row, ct);
        var prepared = ResourceProcessingWorkerEnvelope.Read(row.InputJson).Prepared;
        artifacts.Cleanup(prepared, "worker");
        row.State = "Cancelled"; row.InputJson = "{}"; row.ResultJson = null;
        await store.UpdateAsync(row, ct);
        return true;
    });

    private async Task<bool> ReleaseExpiredWorker(ResourceProcessingJobDbModel row, NodeIdentity self, CancellationToken ct)
    {
        if (row.State == "Running" || row.InputJson == "{}") return false;
        var saved = ResourceProcessingWorkerEnvelope.Read(row.InputJson);
        if (saved.WorkerId == self.NodeId && saved.WorkerEpoch == self.LibraryEpoch) return false;
        row.State = "Conflict"; row.ErrorCode = "EpochChanged";
        row.Error = "The worker's data directory was replaced. Start a new run with its current library.";
        // Fence a runner before removing its artifacts.
        await store.UpdateAsync(row, ct);
        artifacts.Cleanup(saved.Prepared, "worker");
        row.InputJson = "{}"; row.ResultJson = null;
        await store.UpdateAsync(row, ct);
        return true;
    }
}

internal static class ResourceProcessingEndpoint
{
    public static async Task<IActionResult> Run(ControllerBase controller, Func<Task<object>> operation)
    {
        try { return controller.Ok(await operation()); }
        catch (ResourceProcessingException e)
        {
            var status = e.Code is "Conflict" or "JobChanged" or "JobConflict" or "ResourceChanged" or "CatalogChanged" or "EpochChanged" ? 409 :
                e.Code == "BudgetExceeded" ? 413 : e.Code == "JobMissing" ? 404 : 400;
            return controller.StatusCode(status, new {code = e.Code, message = e.Message});
        }
        catch (FederationAccessException e) { return controller.StatusCode(e.StatusCode, new {code = e.ErrorCode, message = e.Message}); }
        catch (ArgumentException e) { return controller.BadRequest(new {code = "InvalidProcessingRequest", message = e.Message}); }
    }
}
