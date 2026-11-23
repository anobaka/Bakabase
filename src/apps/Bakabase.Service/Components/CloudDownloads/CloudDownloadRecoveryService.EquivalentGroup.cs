using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed partial class CloudDownloadRecoveryService
{
    internal sealed record EquivalentGroup(CloudDownloadTarget Owner, CloudDownloadTarget[] Members, string OperationId);

    // A native request can be deliberately reused by several selected parts of the same
    // acquisition. Recover only the original dispatch and require every binding to be
    // explained by the current immutable checkpoint. The creator retains exact source equality.
    internal async Task<EquivalentGroup?> ReadEquivalentGroupAsync(int runId, CloudDownloadTarget target,
        string outputRoot, CancellationToken ct)
    {
        if (target.DownloadTaskId is not { } nativeId || target.SelectedSource == null) return null;
        var run = await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run?.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Running) ||
            run.Status == WorkflowRunStatus.Waiting && run.WaitReason != "cloud.cloudAttention" ||
            WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion") return null;
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
        if (workset == null || workset.Targets.Count is < 2 or > 100 || workset.OutputDirectory != outputRoot) return null;
        var members = workset.Targets.Where(t => t.DownloadTaskId == nativeId).ToArray();
        if (members.Length < 2 || members.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != members.Length ||
            !members.Any(t => t.Id == target.Id && SavedJson(t) == SavedJson(target))) return null;

        var ownerRun = runId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sourceRefs = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == runId &&
            s.Module == "acquisition" && s.EntityKind == "task").Take(2).ToArrayAsync(ct);
        if (sourceRefs.Length != 1 || !int.TryParse(sourceRefs[0].EntityId, out var taskId) ||
            !int.TryParse(sourceRefs[0].Revision, out var revision) ||
            !await db.AcquisitionTasks.AsNoTracking().AnyAsync(a => a.Id == taskId && a.DeletedAt == null &&
                a.PlanRevision == revision && a.WorkflowRunId == runId, ct)) return null;

        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.DownloadTaskId == nativeId, ct);
        if (operation is not {Kind: "download", State: "started" or "starting" or "queued" or "succeeded"} ||
            operation.WorkflowRunId != runId || operation.ReceiptJson != null) return null;
        var owner = members.SingleOrDefault(t => t.Id == operation.TargetId);
        var native = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == nativeId, ct);
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web);
        var configured = string.IsNullOrWhiteSpace(native?.Options) ? null :
            JsonSerializer.Deserialize<CloudDownloadRequest>(native.Options, JsonSerializerOptions.Web);
        if (owner?.SelectedSource == null || native == null || request?.Source == null || configured == null ||
            request.OperationId != operation.Id || request.RemoteEntry != null || request.DestinationRevisions != null ||
            SavedJson(request) != SavedJson(configured) || SavedJson(request.Source) != SavedJson(owner.SelectedSource) ||
            native.ThirdPartyId != CloudSites.Site(request.Source.ProviderId) ||
            !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(request.OutputDirectory), Path.GetFullPath(native.DownloadPath))) return null;

        foreach (var member in members)
        {
            if (member is {Excluded: true} or {TransportTaskId: not null} or {AcquisitionNonce: not null} or {RemoteEntry: not null} ||
                member.SelectedSource is not { } source || member.DownloadOperationId != null && member.DownloadOperationId != operation.Id ||
                member.DownloadLayout != owner.DownloadLayout ||
                SavedJson(source with {Id = request.Source.Id}) != SavedJson(request.Source) ||
                request.AccountId != (member.AccountId ?? source.AccountId) ||
                DownloadArtifactCoordinator.DownloadIntent(member, outputRoot, workspaces) != operation.Id ||
                member.DownloadArtifactId != null || member.ProcessedArtifactId != null || member.PublishedArtifactId != null ||
                member.ProcessingOperationId != null || member.PublicationOperationId != null || member.CleanupReceiptId != null ||
                member.Files.Count != 0 || member.ProcessedFiles.Count != 0 || member.PublishedFiles.Count != 0 || member.ProcessingStepArtifacts.Count != 0)
                return null;
        }
        var slots = members.Select(t => CloudOperationService.OperationSlot(t, "download")).ToHashSet(StringComparer.Ordinal);
        var memberIds = members.Select(t => t.Id).ToArray();
        var transferSlots = members.Select(t => CloudOperationService.OperationSlot(t, "transfer")).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == operation.Id).ToListAsync(ct);
        if (bindings.Count != slots.Count || bindings.Any(b => b.OwnerKind != "workflow" || b.OwnerId != ownerRun || !slots.Contains(b.Slot)) ||
            await db.Set<ArtifactConsumerDbModel>().AsNoTracking().AnyAsync(c => c.OperationId == operation.Id && c.ReleasedAt == null, ct) ||
            await db.ArtifactOperationBindings.AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" && b.OwnerId == ownerRun &&
                transferSlots.Contains(b.Slot), ct) ||
            await db.ManagedDownloadTasks.AsNoTracking().AnyAsync(t => t.SiteTaskId == nativeId &&
                (t.WorkflowRunId != runId || t.OriginKind != "workflow"), ct) ||
            await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.WorkflowRunId == runId && o.Kind == "transfer" &&
                memberIds.Contains(o.TargetId), ct)) return null;
        return new(owner, members, operation.Id);
    }

    private static string SavedJson<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
