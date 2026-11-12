using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionSourceRevisionSafetyResult(bool Safe, string? ReasonCode, string? Version = null);
public interface IAcquisitionSourceRevisionSafety
{
    Task<AcquisitionSourceRevisionSafetyResult> CheckAsync(int runId, CloudDownloadTarget target, string outputDirectory, CancellationToken ct);
}

/// <summary>A failure is not proof of no writes. Reuse the downloader's strict no-payload identity
/// checks, then require a provider capability that also rules out uncertain remote writes.</summary>
public sealed class AcquisitionSourceRevisionSafety(BakabaseDbContext db, CloudDownloadRecoveryService recovery,
    IEnumerable<ICloudDownloadProvider> providers) : IAcquisitionSourceRevisionSafety
{
    public async Task<AcquisitionSourceRevisionSafetyResult> CheckAsync(int runId, CloudDownloadTarget target,
        string outputDirectory, CancellationToken ct)
    {
        var check = await recovery.CheckNoOutputForSourceRevisionAsync(runId, target, outputDirectory, ct);
        if (!check.Candidate) return new(false, check.ReasonCode);
        var owner = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bindings = await db.ArtifactOperationBindings.AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner).ToListAsync(ct);
        // Do not abandon hidden historical credential decisions or partially completed siblings.
        if (bindings.Count != 1 || bindings[0].Slot != CloudOperationService.OperationSlot(target, "download"))
            return new(false, "SOURCE_REVISION_OPERATION_REVIEW_REQUIRED");
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == bindings[0].OperationId, ct);
        var associated = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.WorkflowRunId == runId).ToListAsync(ct);
        if (operation == null || operation.WorkflowRunId != runId || associated.Any(o => o.Id != operation.Id) ||
            await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OperationId == operation.Id &&
                (b.OwnerKind != "workflow" || b.OwnerId != owner), ct) ||
            await db.DownloadResultOwners.AsNoTracking().AnyAsync(o => o.DownloadTaskId == target.DownloadTaskId && o.WorkflowRunId != runId, ct) ||
            await db.ManagedDownloadTasks.AsNoTracking().AnyAsync(o => o.SiteTaskId == target.DownloadTaskId && o.WorkflowRunId != null && o.WorkflowRunId != runId, ct))
            return new(false, "SOURCE_REVISION_SHARED_OPERATION");
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web);
        if (request == null || target.SelectedSource == null ||
            DownloadProviderRouting.Require(providers, target.SelectedSource) is not ICloudDownloadAbandonmentProvider capability)
            return new(false, "SOURCE_REVISION_PROVIDER_REVIEW_REQUIRED");
        var remote = await capability.CheckCanAbandonAsync(request, ct);
        if (!remote.Available) return new(false, remote.Problem ?? "SOURCE_REVISION_REMOTE_WRITE_UNCERTAIN");
        var download = await db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == target.DownloadTaskId, ct);
        return new(true, null, CloudOperationService.Key(JsonSerializer.Serialize(download),
            JsonSerializer.Serialize(operation), JsonSerializer.Serialize(bindings)));
    }
}
