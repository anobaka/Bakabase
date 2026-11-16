using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Moves the first local enqueue of a proven transferred share to a current task allocation.
/// A completed remote transfer is reusable evidence, never authorization to repeat the transfer.</summary>
public sealed class AcquisitionTransferredDownloadRecovery(BakabaseDbContext db, IUserStoragePolicy storage,
    AcquisitionOutputDirectoryResolver outputs, AcquisitionDeliveryDirectoryResolver deliveries,
    AcquisitionStoragePolicyDefaults defaults, FileProcessingWorkspaceResolver workspaces)
{
    internal sealed record Placement(string Proof, DownloadWorkset Workset, IReadOnlyList<string> Directories);

    internal async Task<Placement?> InspectAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel run, DownloadWorkset item,
        CancellationToken ct)
    {
        if (task.DeletedAt != null || task.WorkflowRunId != run.Id || task.PlanJson == null || run.CurrentStepIndex is not {} step ||
            run.PendingSignalJson != null || !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run.Id, task.Id, ct)) return null;
        var definition = WorkflowRunPlans.Read(run.PlanSnapshotJson);
        if (definition?.Nodes.ElementAtOrDefault(step)?.Kind != "download.enqueueSupported" || step == 0 ||
            definition.Nodes[step - 1].Kind != "baidu.transfer") return null;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, WorkflowJson.Options);
        var root = outputs.ResolveNew(null);
        if (plan == null || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ||
            !Path.IsPathFullyQualified(item.OutputDirectory) || Same(root, item.OutputDirectory)) return null;
        ArtifactManifestFiles.RejectLinks(root); storage.EnsureTreeMutationAllowed(root);
        var together = item.Targets.Where(t => !t.Excluded).ToArray();
        if (together.Length == 0 || together.Length > 100 ||
            together.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != together.Length) return null;
        foreach (var node in definition.Nodes.Skip(step).Where(n => n.Kind.StartsWith("download.", StringComparison.Ordinal)))
        {
            var config = JsonSerializer.Deserialize<CloudWorkflowConfig>(node.ConfigJson, WorkflowJson.Options);
            if (config?.ReusePolicy == "force" || config?.OutputDirectory is {} pinned && !Same(pinned, root) ||
                config?.AccountId is {Length: > 0} pinnedAccount && together.Any(t => t.AccountId != pinnedAccount)) return null;
        }
        var policy = defaults.Capture();
        var layout = AcquisitionStageDirectoryResolver.Allocate(task.Id, plan with {OutputDirectory = root, StoragePolicy = policy}, deliveries);
        var priorRuns = AcquisitionDeletionActivityReader.HistoryRuns(task.PlanHistoryJson).Append(run.Id).Distinct().ToArray();
        var ownerId = task.Id.ToString(CultureInfo.InvariantCulture);
        var linkedRuns = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && s.EntityId == ownerId).Select(s => s.WorkflowRunId).ToArrayAsync(ct);
        priorRuns = priorRuns.Concat(linkedRuns).Distinct().ToArray();
        var runKeys = priorRuns.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (await db.DownloadResultOwners.AnyAsync(o => o.AcquisitionTaskId == task.Id || priorRuns.Contains(o.WorkflowRunId), ct) ||
            await db.ManagedDownloadTasks.AnyAsync(d => d.WorkflowRunId != null && priorRuns.Contains(d.WorkflowRunId.Value), ct) ||
            await db.ArtifactOperationBindings.AnyAsync(b => b.OwnerKind == "workflow" && runKeys.Contains(b.OwnerId) &&
                !b.Slot.EndsWith(":transfer"), ct) ||
            await db.CloudDownloadOperations.AnyAsync(o => o.Kind == "download" && priorRuns.Contains(o.WorkflowRunId), ct)) return null;
        var directories = new List<string>(); var proofs = new List<string>(); var next = item.Targets.ToList();
        foreach (var target in together)
        {
            var source = target.SelectedSource;
            var part = plan.Parts.SingleOrDefault(p => p.Id == target.Id);
            var selected = part?.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId);
            if (source == null || target.RemoteEntry is not {} remote || string.IsNullOrEmpty(target.AccountId) ||
                remote.AccountId != target.AccountId || remote.ProviderId != source.ProviderId || target.DownloadTaskId != null ||
                target.TransportTaskId != null || target.DownloadOperationId != null || target.DownloadArtifactId != null ||
                target.ProcessedArtifactId != null || target.PublishedArtifactId != null || target.ProcessingOperationId != null ||
                target.PublicationOperationId != null || target.AcquisitionNonce != null || target.Files.Count != 0 ||
                target.ProcessedFiles.Count != 0 || target.PublishedFiles.Count != 0 || target.DownloadDirectory != null ||
                target.ProcessedDirectory != null || target.PublishedDirectory != null ||
                selected == null || selected.Id != source.Id || selected.Url != source.Url ||
                selected.AccessCode != source.AccessCode || selected.AccountId != source.AccountId || selected.ProviderId != source.ProviderId) return null;
            var transferConfig = JsonSerializer.Deserialize<CloudWorkflowConfig>(definition.Nodes[step - 1].ConfigJson, WorkflowJson.Options);
            if (transferConfig == null || transferConfig.ReusePolicy == "force") return null;
            var sourceKey = DownloadArtifactCoordinator.Source(target);
            var transferId = CloudOperationService.Key("transfer.v2", sourceKey, transferConfig.RemoteDirectory);
            var bound = await db.ArtifactOperationBindings.AsNoTracking().SingleOrDefaultAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == run.Id.ToString() && b.Slot == CloudOperationService.OperationSlot(target, "transfer"), ct);
            var transfer = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == transferId, ct);
            if (bound?.OperationId != transferId || transfer is not {Kind: "transfer", State: "succeeded", ReceiptJson: not null} ||
                transfer.WorkflowRunId != run.Id || transfer.TargetId != target.Id) return null;
            var receipt = JsonSerializer.Deserialize<CloudTransferReceipt>(transfer.ReceiptJson, WorkflowJson.Options);
            var request = JsonSerializer.Deserialize<CloudTransferRequest>(transfer.RequestJson, WorkflowJson.Options);
            if (receipt?.OperationId != transferId || receipt.Entry != remote || request?.OperationId != transferId ||
                request.AccountId != target.AccountId || request.RemoteDirectory != transferConfig.RemoteDirectory.TrimEnd('/') + "/" + transferId ||
                CloudSourceIdentity.Key(request.Source, request.AccountId) != sourceKey) return null;
            var allocation = layout.Parts[target.Id];
            var location = plan.StageDirectories?.Parts.GetValueOrDefault(target.Id)?.Download is {BindingId: not null} manual
                ? manual : allocation.Download!;
            var destination = location.Directory;
            if (!ArtifactCleanupPathReferences.StrictlyUnder(destination, root)) return null;
            ArtifactManifestFiles.RejectLinks(destination); storage.EnsureTreeMutationAllowed(destination);
            if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) return null;
            var originalIntent = DownloadArtifactCoordinator.DownloadIntent(target, item.OutputDirectory, workspaces);
            var originalLocation = CloudStageDirectoryPath.Download(target) ?? target.DownloadDirectoryScope?.Directory ??
                Path.Combine(item.OutputDirectory, target.StoragePolicy == null ? "cloud-" + originalIntent[..16] :
                    ArtifactStoragePolicies.RenderDownload(target.StoragePolicy, target.Title, originalIntent, source.ProviderId));
            // A remounted shared parent may contain unrelated downloads. Only the bound target
            // directory (including a partial payload) is evidence that this transfer started.
            if (storage.IsPathAllowed(originalLocation))
            {
                ArtifactManifestFiles.RejectLinks(originalLocation);
                if (File.Exists(originalLocation) || Directory.Exists(originalLocation) &&
                    Directory.EnumerateFileSystemEntries(originalLocation).Any()) return null;
            }
            var newTarget = target with {DownloadDirectoryScope = location.Allocation,
                StageDirectories = (target.StageDirectories ?? new()) with {Download = location with
                {BindingId = ArtifactManifestFiles.Hash(task.Id + ":" + target.Id + ":" + destination)[..32]}},
                ProcessingDirectoryScope = target.ProcessingDirectoryScope ?? allocation.Processing?.Allocation,
                StoragePolicy = target.StoragePolicy ?? policy, PublicationTargetDirectory = target.PublicationTargetDirectory ??
                    plan.DeliveryLayout?.PartDirectories.GetValueOrDefault(target.Id)};
            var newIntent = DownloadArtifactCoordinator.DownloadIntent(newTarget, root, workspaces);
            var site = CloudSites.Site(source.ProviderId);
            if (await db.CloudDownloadOperations.AnyAsync(o => o.Kind == "download" &&
                    (o.Id == originalIntent || o.Id == newIntent || o.RequestJson.Contains(transferId) ||
                     o.RequestJson.Contains(source.Url)), ct) ||
                await db.DownloadTasks.AnyAsync(d => d.ThirdPartyId == site && (d.Key == source.Url ||
                    d.Options != null && (d.Options.Contains(originalIntent) || d.Options.Contains(newIntent) ||
                       d.Options.Contains(transferId) || d.Options.Contains(source.Url))), ct) ||
                await db.DownloadResults.AnyAsync(r => r.SourceKey == source.Url, ct) ||
                await db.ArtifactOperations.AnyAsync(o => o.IntentKey == originalIntent || o.IntentKey == newIntent ||
                    o.Kind == "download" && o.InputFingerprint == sourceKey, ct)) return null;
            directories.Add(destination);
            if (newTarget.ProcessingDirectoryScope is {} processing) directories.Add(processing.Directory);
            if (newTarget.PublicationTargetDirectory is {} delivery) directories.Add(delivery);
            proofs.Add(CloudOperationService.Key(target.Id, sourceKey, transferId, transfer.ReceiptJson,
                originalIntent, newIntent, destination, newTarget.ProcessingDirectoryScope?.Directory ?? "",
                newTarget.PublicationTargetDirectory ?? "", JsonSerializer.Serialize(newTarget.StoragePolicy, WorkflowJson.Options)));
            next[next.IndexOf(target)] = newTarget;
        }
        var token = CloudOperationService.Key("transferred-first-download.v1", task.Id.ToString(CultureInfo.InvariantCulture),
            task.PlanRevision.ToString(CultureInfo.InvariantCulture), run.Id.ToString(CultureInfo.InvariantCulture),
            step.ToString(CultureInfo.InvariantCulture), item.OutputDirectory, root, string.Join("|", proofs));
        return new(token, item with {OutputDirectory = root, Targets = next, NoRepeatExistingDownloads = true}, directories);
    }

    internal async Task<DownloadWorkset> RestoreAsync(int runId, DownloadWorkset item, string proof, CancellationToken ct)
    {
        var run = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == runId, ct);
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.DeletedAt == null && t.WorkflowRunId == runId, ct);
        var placement = task == null ? null : await InspectAsync(task, run, item, ct);
        if (placement == null || placement.Proof != proof) throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        return placement.Workset;
    }

    private static bool Same(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));
}
