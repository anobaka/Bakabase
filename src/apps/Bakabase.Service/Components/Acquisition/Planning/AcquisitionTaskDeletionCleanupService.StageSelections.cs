using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    private sealed record StageCleanupSelection(string Id, string PartId, string Stage, AcquisitionStageDirectory Entry,
        bool Current = false, FileProcessingWorkspaceResolver.Location? Workspace = null, bool HistoricalDeliveryLayout = false)
    {
        public AcquisitionCleanupScopes Scopes { get; init; } = Stage == "delivery" ? AcquisitionCleanupScopes.Delivery : AcquisitionCleanupScopes.Intermediate;
    }
    private sealed record StageCleanupSelectionSet(IReadOnlyList<StageCleanupSelection> Selected,
        IReadOnlyList<string> ProtectedRoots, IReadOnlyList<string> ConfiguredRoots,
        IReadOnlyList<string> PublicationRoots, IReadOnlyList<string> DeliveryLayoutRoots);

    internal async Task AuthorizeWriterDirectoriesAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners,
        bool cleanupIntermediates, bool deleteDeliveredFiles, Func<string, Task>? guardDirectory, CancellationToken ct)
    {
        if (guardDirectory == null) return;
        if (JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web) is {} plan)
        {
            var selections = await StageCleanupSelectionsAsync(task, plan, owners, cleanupIntermediates, deleteDeliveredFiles, ct);
            foreach (var selected in selections.Selected)
                await EnsureDirectoryAuthorizedAsync(selected.Entry.Directory, guardDirectory);
        }
        if (deleteDeliveredFiles && !string.IsNullOrWhiteSpace(task.TargetDirectory))
            await EnsureDirectoryAuthorizedAsync(task.TargetDirectory, guardDirectory);
    }

    // An exact binding/allocation is authority; a task title, directory prefix or
    // artifact's containing folder is not. This bounded metadata discovery replaces
    // the old early return that silently forgot roots after a directory revision.
    private async Task<StageCleanupSelectionSet> StageCleanupSelectionsAsync(AcquisitionTaskDbModel task,
        AcquisitionPlan current, IReadOnlyList<int> owners, bool cleanupIntermediates, bool deleteDeliveredFiles,
        CancellationToken ct)
    {
        var history = JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson ?? "[]", JsonSerializerOptions.Web) ?? [];
        if (history.Length > 100) throw new InvalidDataException("CLEANUP_LINEAGE_TOO_LARGE");
        var plans = history.Select(h => h.Plan).Append(current).ToArray();
        var selections = new List<StageCleanupSelection>();
        var protectedRoots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var configuredRoots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var layoutRoots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var publicationRoots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var allPartIds = plans.SelectMany(p => p.Parts).Select(p => p.Id).Distinct(StringComparer.Ordinal)
            .OrderByDescending(p => p.Length).ToArray();
        bool Selected(string stage) => stage == "delivery" ? deleteDeliveredFiles : cleanupIntermediates;
        static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        void Add(string identity, string partId, string stage, AcquisitionStageDirectory entry, bool isCurrent = false,
            FileProcessingWorkspaceResolver.Location? workspace = null, bool historicalDeliveryLayout = false)
        {
            var normalized = entry with {Directory = Full(entry.Directory)};
            if (!Selected(stage)) {protectedRoots.Add(normalized.Directory); return;}
            selections.Add(new(identity, partId, stage, normalized, isCurrent, workspace, historicalDeliveryLayout));
        }
        foreach (var plan in plans)
        {
            foreach (var configured in new[] {plan.OutputDirectory, plan.StoragePolicy?.ProcessingWorkspaceDirectory,
                         plan.DeliveryLayout?.RootDirectory}.Concat(plan.Parts.Select(p => p.StoragePolicy?.ProcessingWorkspaceDirectory)))
                if (!string.IsNullOrWhiteSpace(configured)) configuredRoots.Add(Full(configured));
            if (plan.DeliveryLayout is { } layout && layout.TaskId == task.Id)
            {
                AcquisitionDeliveryDirectoryResolver.ValidateLayout(layout, plan.Parts.Select(p => p.Id).ToArray());
                foreach (var directory in layout.PartDirectories.Values) layoutRoots.Add(Full(directory));
            }
            if (plan.StageDirectories is not { } stages) continue;
            AcquisitionStageDirectoryResolver.Validate(stages, plan);
            if (stages.TaskId != task.Id) throw new InvalidDataException("CLEANUP_DIRECTORY_OWNERSHIP_UNCONFIRMED");
            var isCurrent = ReferenceEquals(plan, current);
            foreach (var (partId, part) in stages.Parts)
            foreach (var (stage, entry) in new[] {("download", part.Download), ("processing", part.Processing), ("delivery", part.Delivery)})
            {
                if (entry == null) continue;
                // Even an unproven old delivery protects its exact path when final
                // files were not selected. A historical projection alone does not
                // authorize recursive deletion of an arbitrary input container.
                if (!Selected(stage)) {protectedRoots.Add(Full(entry.Directory)); continue;}
                if (!isCurrent && entry.Evidence is not ("taskAllocation" or "taskLayout" or "downloadRecord" or
                        "userDirectoryRevision" or "plannedDelivery" or "deliveryRecord")) continue;
                var historicalDeliveryLayout = !isCurrent && stage == "delivery" &&
                    plan.DeliveryLayout is { } delivery && delivery.TaskId == task.Id &&
                    delivery.PartDirectories.TryGetValue(partId, out var partPath) &&
                    ArtifactManifestFiles.PathComparer.Equals(Full(partPath), Full(entry.Directory));
                Add("binding:" + ArtifactManifestFiles.Hash(JsonSerializer.Serialize(new {partId, stage, entry})),
                    partId, stage, entry, isCurrent, historicalDeliveryLayout: historicalDeliveryLayout);
            }
        }
        var ownerKeys = owners.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b =>
            b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId)).ToListAsync(ct);
        var operationIds = bindings.Select(b => b.OperationId).Distinct().ToArray();
        var allBindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => operationIds.Contains(b.OperationId)).ToListAsync(ct);
        var ownedOperations = operationIds.Where(id => allBindings.Where(b => b.OperationId == id)
            .All(b => b.OwnerKind == "workflow" && ownerKeys.Contains(b.OwnerId))).ToHashSet(StringComparer.Ordinal);
        string? Part(string operationId) => allBindings.Where(b => b.OperationId == operationId)
            .SelectMany(b => allPartIds.Where(id => b.Slot.StartsWith(id + ":", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        var publicationIds = bindings.Where(b => b.Slot.EndsWith(":artifact.publish", StringComparison.Ordinal))
            .Select(b => b.OperationId).Where(ownedOperations.Contains).ToArray();
        var publications = await db.Artifacts.AsNoTracking().Where(a => publicationIds.Contains(a.OperationId) &&
            a.Kind == "publishedFiles").Select(a => a.Id).ToArrayAsync(ct);
        foreach (var id in publications)
        {
            var artifact = await artifacts.GetArtifactAsync(id, ct);
            if (artifact == null) continue;
            var directory = Full(artifact.RootDirectory);
            publicationRoots.Add(directory);
            if (!deleteDeliveredFiles) {protectedRoots.Add(directory); continue;}
            if (Part(artifact.OperationId) is { } partId)
                Add("publication:" + id, partId, "delivery", new(directory, "borrowed", "publicationRecord"));
        }
        // A reviewed cross-volume resume keeps its original source on disk. Retain that exact
        // directory in task-wide cleanup history even after the native writer points elsewhere.
        var retainedSources = await (from op in db.CloudDownloadOperations.AsNoTracking()
            join native in db.DownloadTasks.AsNoTracking() on op.DownloadTaskId equals native.Id
            where owners.Contains(op.WorkflowRunId) && op.Kind == "download" && native.Options == op.RequestJson &&
                native.Options != null && native.Options.Contains("\"reuseInventoryFingerprint\"")
            select new {op.Id, op.TargetId, native.DownloadPath, native.Options, Site = native.Id}).ToListAsync(ct);
        foreach (var row in retainedSources.Where(r => allPartIds.Contains(r.TargetId, StringComparer.Ordinal)))
        {
            var request = JsonSerializer.Deserialize<CloudDownloadRequest>(row.Options!, JsonSerializerOptions.Web);
            if (request == null || request.OperationId != row.Id ||
                !ArtifactManifestFiles.PathComparer.Equals(CloudDownloadDestinations.ExecutionDirectory(request), Full(row.DownloadPath))) continue;
            foreach (var revision in CloudDownloadDestinations.RetainedSources(request))
                Add("transport-source:" + row.Site + ":" + revision.RevisionId, row.TargetId, "download",
                    new(revision.PreviousDirectory, "borrowed", "retainedDownloadSource") {BindingId = revision.RevisionId});
        }
        if (cleanupIntermediates)
        {
            // A detached current download binding does not erase the native transport's
            // exact completed destination. Never expand this path to its configured root.
            var transports = await db.CloudDownloadOperations.AsNoTracking().Where(o => owners.Contains(o.WorkflowRunId) &&
                o.Kind == "download" && o.DownloadTaskId != null).Select(o => new {o.DownloadTaskId, o.TargetId}).Distinct().ToListAsync(ct);
            foreach (var transport in transports.Where(t => allPartIds.Contains(t.TargetId, StringComparer.Ordinal)))
            {
                var directory = await db.DownloadTasks.AsNoTracking().Where(d => d.Id == transport.DownloadTaskId)
                    .Select(d => d.DownloadPath).SingleOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(directory))
                    Add("transport:" + transport.DownloadTaskId, transport.TargetId, "download", new(directory, "borrowed", "downloadRecord"));
            }
            if (workspaces != null)
            {
                var operations = await db.ArtifactOperations.AsNoTracking().Where(o => operationIds.Contains(o.Id) &&
                    o.Kind == "files.process" && o.Attempt > 0).ToListAsync(ct);
                foreach (var operation in operations.Where(o => ownedOperations.Contains(o.Id) && HexKey(o.Id)))
                {
                    var partId = Part(operation.Id);
                    if (partId == null) continue;
                    void AddLocation(FileProcessingWorkspaceResolver.Location? location)
                    {
                        if (location == null) return;
                        Add("workspace:" + location.Kind + ":" + location.Key + ":" + location.Attempt,
                            partId, "processing", new(location.Directory, "exclusive", "ownedWorkspace"), workspace: location);
                    }
                    // The durable locator and installation marker prove the exact private
                    // attempt. No probing of directory names or broad .bakabase-work roots.
                    await AddKnownLocation(() => workspaces.InspectOwnedLocationForStageCleanupAsync("operations", operation.Id,
                        operation.Attempt, ct), AddLocation);
                    if (operation.OutputArtifactId == null || await artifacts.GetArtifactAsync(operation.OutputArtifactId, ct) is not { } output ||
                        output.Kind != "processedFiles" || !output.Managed) continue;
                    var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(output.MetadataJson ?? "null", JsonSerializerOptions.Web);
                    if (metadata?.CompletedThroughStepId == null) continue;
                    var processingPlan = JsonSerializer.Deserialize<FileProcessingPlan>(metadata.PlanJson, JsonSerializerOptions.Web);
                    if (processingPlan == null) continue;
                    var configured = metadata.WorkspaceDirectoryScope?.Directory ?? metadata.StoragePolicy?.ProcessingWorkspaceDirectory;
                    await AddKnownLocation(async () =>
                        await workspaces.InspectOwnedCheckpointForStageCleanupAsync(FileArtifactProcessor.CheckpointKey(operation.InputFingerprint, processingPlan),
                            operation.Id, operation.Attempt, configured, ct) ??
                        await workspaces.InspectOwnedCheckpointForStageCleanupAsync(ArtifactManifestFiles.Hash(operation.InputFingerprint + "\n" + metadata.PlanJson),
                            operation.Id, operation.Attempt, configured, ct), AddLocation);
                }
            }
        }
        // Prefer the current binding for exact duplicates. It remains visible as the
        // task's ordinary stage, while authenticated larger historical roots may cover it.
        var distinct = selections.OrderByDescending(s => s.Entry.Evidence == "retainedDownloadSource").ThenByDescending(s => s.Current).GroupBy(s => s.Entry.Directory, ArtifactManifestFiles.PathComparer)
            .Select(g => g.First() with {Scopes = g.Aggregate(AcquisitionCleanupScopes.None, (scopes, entry) => scopes | entry.Scopes)}).ToArray();
        return new(distinct, protectedRoots.ToArray(), configuredRoots.ToArray(), publicationRoots.ToArray(), layoutRoots.ToArray());
    }

    private static async Task AddKnownLocation(Func<Task<FileProcessingWorkspaceResolver.Location?>> read,
        Action<FileProcessingWorkspaceResolver.Location?> add)
    {
        try { add(await read()); }
        // Missing locators are not permission to guess a parent. Existing stage bindings
        // still report unavailable storage; private locations without proof stay untouched.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
    }
}
