using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Durable path references outlive consumer leases. Shared by registered-artifact
/// retirement and executor-private reclamation, including completed tasks and relocated roots.</summary>
internal sealed partial class ArtifactCleanupReferenceGuard(BakabaseDbContext db, FileProcessingWorkspaceResolver? workspaces = null,
    bool cachePreviewReads = false, Action<string>? referenceFound = null)
{
    // Only a short-lived read-only preview opts in. Execution creates a fresh uncached
    // guard at every safety phase, so references added after confirmation still protect files.
    private readonly Dictionary<string, object>? previewReads = cachePreviewReads ? new(StringComparer.Ordinal) : null;
    internal ArtifactCleanupReferenceGuard CreatePreviewSnapshot() => new(db, workspaces, cachePreviewReads: true);
    private async Task<T> ReadAsync<T>(string key, Func<Task<T>> read, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (previewReads?.TryGetValue(key, out var cached) == true) return (T) cached;
        var value = await read();
        if (previewReads != null) previewReads.Add(key, value!);
        return value;
    }
    private static string ScopeKey(IReadOnlyList<int> ids) => string.Join(",", ids.Distinct().Order());
    public async Task<bool> HasReferencesAsync(ArtifactRecord source, IReadOnlyList<int> owners, CancellationToken ct,
        IReadOnlyList<string>? privatePaths = null, IReadOnlyList<int>? previewSupersededRuns = null,
        int? directoryOwnerTaskId = null, int? directoryOwnerRunId = null, bool deletedDirectoryOwner = false,
        int? directoryDownloadTaskId = null, bool explicitTaskDirectoryCleanup = false, bool abandonUnfinishedFiles = false,
        string? processingPartId = null)
    {
        bool Found(string location) { referenceFound?.Invoke(location); return true; }
        var aliases = workspaces?.ReadRelocations() ?? [];
        var paths = new ArtifactCleanupPathReferences(privatePaths ?? [source.RootDirectory], aliases, ct);
        var roots = paths.Roots;
        string Relocate(string path) => paths.Relocate(path);
        bool RefersPath(string value, bool ancestors = true) => paths.Refers(value, ancestors);
        bool Refers(string? json, bool allPathsAreReferences = false, bool frozenWorkflowPlan = false,
            Func<JsonElement, IReadOnlySet<JsonElement>>? allocationContract = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            using var doc = JsonDocument.Parse(json);
            IReadOnlyDictionary<JsonElement, IReadOnlyList<string>> nestedFields = new Dictionary<JsonElement, IReadOnlyList<string>>();
            var allocations = allocationContract?.Invoke(doc.RootElement) ?? (frozenWorkflowPlan
                ? ArtifactWorkflowParameterReferences.AllocationValues(doc.RootElement, ct, out nestedFields) : null);
            return Visit(doc.RootElement, 0, true, allocations);
            bool Text(string text, int depth, bool ancestors, JsonElement? location = null)
            {
                if (privatePaths == null && text == source.Id || RefersPath(text, ancestors)) return true;
                if (text.StartsWith('{') || text.StartsWith('['))
                {
                    try
                    {
                        using var nested = JsonDocument.Parse(text);
                        var known = location is { } at && nestedFields.TryGetValue(at, out var names) && nested.RootElement.ValueKind == JsonValueKind.Object
                            ? nested.RootElement.EnumerateObject().Where(p => names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).Select(p => p.Value).ToHashSet()
                            : null;
                        return Visit(nested.RootElement, depth + 1, ancestors, known);
                    }
                    catch (JsonException)
                    {
                        return (privatePaths == null ? roots.Append(source.Id) : roots).Concat(aliases.Select(a => a.SourceRoot))
                            .Any(p => text.Contains(p, StringComparison.Ordinal) || text.Contains(JsonSerializer.Serialize(p)[1..^1], StringComparison.Ordinal));
                    }
                }
                return false;
            }
            bool IsConfiguration(string name) => name is "storagepolicy" or "deliverysettings" or "downloadsettings" or
                "storagesettings" or "defaultoptions" or "defaults" or "processingworkspacedirectory" or
                "workingrootdirectory" or "finaldeliverydirectory" or "downloadrootdirectory";
            bool Visit(JsonElement value, int depth, bool ancestors, IReadOnlySet<JsonElement>? roles)
            {
                ct.ThrowIfCancellationRequested();
                if (depth > 64) return true;
                if (!allPathsAreReferences && roles?.Contains(value) == true) ancestors = false;
                return value.ValueKind switch
                {
                    // A storage-policy root configures where future work may be created; it
                    // does not claim every task already underneath that shared directory.
                    // Actual input paths (and unknown JSON outside known config blocks)
                    // remain conservative ancestor references. Path keys are references too.
                    JsonValueKind.Object => value.EnumerateObject().Any(p => Text(p.Name, depth + 1, true) ||
                        Visit(p.Value, depth + 1, allPathsAreReferences || ancestors &&
                            !IsConfiguration(p.Name.Replace("_", "").Replace("-", "").ToLowerInvariant()),
                            ArtifactDirectoryScopeReferences.AllocationRoot(p.Name, p.Value, value) is { } allocationRoot
                                ? new HashSet<JsonElement>(roles ?? new HashSet<JsonElement>()) {allocationRoot} : roles)),
                    JsonValueKind.Array => value.EnumerateArray().Any(v => Visit(v, depth + 1, ancestors, roles)),
                    JsonValueKind.String => Text(value.GetString()!, depth, ancestors, value),
                    _ => false
                };
            }
        }
        bool RefersCloudRequest(string? json, string? operationId = null, string? providerId = null, string? outputDirectory = null,
            IReadOnlyList<string>? privateAllocations = null, string? stageDownloadOperation = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || outputDirectory != null && providerId == null)
                return Refers(json, true);
            var properties = doc.RootElement.EnumerateObject().ToArray();
            var names = properties.Select(p => p.Name).ToArray();
            var allowed = new[] {"operationId", "source", "accountId", "remoteEntry", "outputDirectory", "workspaceDirectory", "destinationRevisions"};
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
                names.Any(n => !allowed.Contains(n, StringComparer.OrdinalIgnoreCase))) return Refers(json, true);
            CloudDownloadRequest? request;
            string? executionDirectory;
            try
            {
                request = JsonSerializer.Deserialize<CloudDownloadRequest>(json, JsonSerializerOptions.Web);
                if (request == null || request.DestinationRevisions?.Any(r => r == null) == true) return Refers(json, true);
                // Physical placement revisions are a typed, chained transport contract.
                // Unknown members must not disappear in deserialization and acquire the
                // relaxed allocation role merely by being called rootDirectory.
                foreach (var revisions in properties.Where(p => p.Name.Equals("destinationRevisions", StringComparison.OrdinalIgnoreCase)))
                {
                    if (revisions.Value.ValueKind == JsonValueKind.Null) continue;
                    if (revisions.Value.ValueKind != JsonValueKind.Array) return Refers(json, true);
                    foreach (var revision in revisions.Value.EnumerateArray())
                    {
                        if (revision.ValueKind != JsonValueKind.Object) return Refers(json, true);
                        var fields = revision.EnumerateObject().Select(p => p.Name).ToArray();
                        var contract = new[] {"revisionId", "previousDirectory", "directory", "rootDirectory", "appliedAt", "previousFingerprint"};
                        if (fields.Length != contract.Length || fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length ||
                            fields.Any(name => !contract.Contains(name, StringComparer.OrdinalIgnoreCase))) return Refers(json, true);
                    }
                }
                executionDirectory = CloudDownloadDestinations.ExecutionDirectory(request);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
            { return Refers(json, true); }
            if (request?.Source == null || string.IsNullOrWhiteSpace(request.OperationId) ||
                string.IsNullOrWhiteSpace(request.Source.Id) || string.IsNullOrWhiteSpace(request.Source.Url) ||
                !Path.IsPathFullyQualified(request.OutputDirectory ?? "") ||
                !CloudSites.Sources.Any(s => s.ProviderId == request.Source.ProviderId) && request.Source.ProviderId != "exHentai" ||
                operationId != null && request.OperationId != operationId ||
                providerId != null && request.Source.ProviderId != providerId ||
                outputDirectory != null && !Same(Relocate(executionDirectory!), Relocate(outputDirectory)) ||
                request.WorkspaceDirectory is {Length: > 0} workspace && !Path.IsPathFullyQualified(workspace))
                return Refers(json, true);
            // Only these exact typed locations allocate sibling staging/download folders.
            // Frozen/previous/current concrete destinations and all nested inputs remain
            // references. Even a valid allocation equal to or inside the candidate blocks.
            bool RegisteredContainer(JsonProperty field) => privateAllocations != null &&
                field.Value.ValueKind == JsonValueKind.String && Path.IsPathFullyQualified(field.Value.GetString()!) &&
                privateAllocations.Any(path => Same(Relocate(field.Value.GetString()!), path));
            // The completed download allocated this part's processing directory for the
            // next stage. Its own workspace setting is not a consumer of the stage output.
            // The native download path, actual files and all other request fields still count.
            bool OwnStageWorkspace(JsonProperty p) => stageDownloadOperation == request.OperationId &&
                p.Name.Equals("workspaceDirectory", StringComparison.OrdinalIgnoreCase) &&
                p.Value.ValueKind == JsonValueKind.String && Path.IsPathFullyQualified(p.Value.GetString()!) &&
                Same(Relocate(p.Value.GetString()!), Relocate(source.RootDirectory));
            return properties.Where(p => !OwnStageWorkspace(p)).Any(p => p.Name.Equals("workspaceDirectory", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) && RegisteredContainer(p)
                ? p.Value.ValueKind == JsonValueKind.String && RefersPath(p.Value.GetString()!, false)
                : p.Name.Equals("destinationRevisions", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Any(revision => revision.EnumerateObject().Any(field =>
                        field.Name.Equals("rootDirectory", StringComparison.OrdinalIgnoreCase) ||
                            (field.Name.Equals("directory", StringComparison.OrdinalIgnoreCase) ||
                             field.Name.Equals("previousDirectory", StringComparison.OrdinalIgnoreCase)) && RegisteredContainer(field)
                            ? RefersPath(field.Value.GetString()!, false) : Refers(field.Value.GetRawText(), true)))
                : Refers(p.Value.GetRawText(), true));
        }
        // Completed consumers are deliberately included: an expired consumer lease is not
        // proof that another task relinquished its saved continuation or local input.
        var sourceOwners = await ReadAsync("owners", () => db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" && s.EntityKind == "task")
            .Select(s => new {s.WorkflowRunId, s.EntityId, s.Revision}).ToListAsync(ct), ct);
        var ownership = sourceOwners.GroupBy(s => s.WorkflowRunId).Where(g => g.Select(s => (s.EntityId, s.Revision)).Distinct().Count() == 1)
            .Select(g => g.First()).Where(s => int.TryParse(s.EntityId, out _) && int.TryParse(s.Revision, out _))
            .Select(s => (TaskId: int.Parse(s.EntityId), RunId: s.WorkflowRunId, Revision: int.Parse(s.Revision))).ToHashSet();
        // Explicit task-directory edits have no artifact owner before their first run.
        // Only this fixed task's own plan is excluded, after validating the complete run
        // scope. Ordinary cleanup callers never pass this option.
        if (directoryOwnerTaskId is {} directoryOwner && (directoryOwner <= 0 ||
            owners.Any(run => ownership.Count(o => o.RunId == run && o.TaskId == directoryOwner) != 1) ||
            ownership.Any(o => o.TaskId == directoryOwner && !owners.Contains(o.RunId)) ||
            !await db.AcquisitionTasks.AnyAsync(t => t.Id == directoryOwner && (t.DeletedAt != null) == deletedDirectoryOwner &&
                (t.WorkflowRunId == null || owners.Contains(t.WorkflowRunId.Value)), ct)))
            return Found("invalid-directory-task-scope");
        // Runtime stage commits read their own current stage while moving its exact output
        // selection. This is not a cleanup exemption: the caller must prove this task's
        // current run, and all foreign runs/resources/configuration remain references.
        if (directoryOwnerRunId is {} executing && (directoryOwnerTaskId is not {} taskOwner ||
            !owners.Contains(executing) || ownership.Count(o => o.RunId == executing && o.TaskId == taskOwner) != 1 ||
            !await db.AcquisitionTasks.AnyAsync(t => t.Id == taskOwner && t.DeletedAt == null && t.WorkflowRunId == executing, ct)))
            return Found("invalid-directory-run-scope");
        // Only a confirmed whole-directory deletion can release this task's historical
        // transport receipts. Rebinding, runtime moves and ordinary artifact retirement
        // never receive this exemption, even if they happen to name the same directory.
        if (explicitTaskDirectoryCleanup && (directoryOwnerTaskId is not { } cleanupOwner ||
            directoryOwnerRunId != null || privatePaths != null || owners.Count == 0 ||
            !await db.AcquisitionTasks.AnyAsync(t => t.Id == cleanupOwner &&
                (t.Status == AcquisitionStatus.Completed || abandonUnfinishedFiles), ct) ||
            await db.WorkflowRuns.CountAsync(r => owners.Contains(r.Id), ct) != owners.Distinct().Count() ||
            await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.PendingSignalJson != null ||
                 r.Status == WorkflowRunStatus.Waiting && !(previewSupersededRuns ?? Array.Empty<int>()).Contains(r.Id)), ct)))
            return Found("invalid-directory-cleanup-scope");
        var privateAllocation = await PrivateProcessingAllocationAsync(source, privatePaths, ownership, ct);
        var previewInactive = previewSupersededRuns ?? [];
        var active = await ReadAsync("active:" + ScopeKey(previewInactive), () => db.WorkflowRuns.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Pending ||
                r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting &&
                (!previewInactive.Contains(r.Id) || r.PendingSignalJson != null))
            .Select(r => new {r.Id, r.PayloadJson, r.CurrentItemJson, r.WaitPromptJson, r.PlanSnapshotJson, r.PendingSignalJson}).ToListAsync(ct), ct);
        var activeIds = active.Select(r => r.Id).ToHashSet();
        var runOwners = ownership.Where(o => activeIds.Contains(o.RunId)).ToDictionary(o => o.RunId);
        var ownerIds = runOwners.Values.Select(o => o.TaskId).Distinct().ToList();
        var plans = await ReadAsync("plans:" + ScopeKey(previewInactive), () => db.AcquisitionTasks.AsNoTracking()
            .Where(t => t.DeletedAt == null || ownerIds.Contains(t.Id))
            .Select(t => new {t.Id, t.DeletedAt, t.WorkflowRunId, t.PlanRevision, t.PlanJson, t.PlanHistoryJson,
                t.BuilderStateJson, t.PreparedContextJson, t.TargetDirectory}).ToListAsync(ct), ct);
        foreach (var plan in plans.Where(p => p.DeletedAt == null && p.Id != directoryOwnerTaskId &&
                     (explicitTaskDirectoryCleanup || p.WorkflowRunId == null || !owners.Contains(p.WorkflowRunId.Value))))
        {
            var scope = privateAllocation is { } owned && owned.TaskId == plan.Id &&
                plan.WorkflowRunId is { } current && owned.RunIds.Contains(current) ? owned.Scope : null;
            IReadOnlySet<JsonElement> WithPrivateAllocation(JsonElement document, IReadOnlySet<JsonElement> values)
            {
                if (scope == null) return values;
                var combined = new HashSet<JsonElement>(values);
                combined.UnionWith(ArtifactAcquisitionAllocationReferences.OwnedProcessingValues(document, plan.PlanJson, plan.Id, scope, ct));
                return combined;
            }
            if (Refers(plan.PlanJson, allocationContract: document => WithPrivateAllocation(document,
                    ArtifactAcquisitionAllocationReferences.PlanValues(document, ct, plan.Id)))) return Found($"task:{plan.Id}:plan");
            if (Refers(plan.BuilderStateJson, allocationContract: document => WithPrivateAllocation(document,
                    ArtifactAcquisitionAllocationReferences.QueueValues(document,
                    new(plan.Id, plan.WorkflowRunId, plan.PlanRevision, plan.PlanJson, plan.PlanHistoryJson), ownership, ct)))) return Found($"task:{plan.Id}:builder");
            if (Refers(plan.PreparedContextJson)) return Found($"task:{plan.Id}:prepared");
            if (plan.TargetDirectory != null && RefersPath(plan.TargetDirectory, false)) return Found($"task:{plan.Id}:target");
        }
        var plansById = plans.ToDictionary(p => p.Id);
        foreach (var run in active)
        {
            if (run.Id == directoryOwnerRunId) continue;
            // Superseded and soft-deleted tasks can retain waiting runs. Their original
            // owned plan still identifies the allocation root. Concrete inputs and
            // pending answers must be checked even when the task was removed.
            var allocation = runOwners.TryGetValue(run.Id, out var binding) && plansById.TryGetValue(binding.TaskId, out var owner)
                ? ArtifactAcquisitionAllocationReferences.OwnedPlanDirectory(
                    new(owner.Id, owner.WorkflowRunId, owner.PlanRevision, owner.PlanJson, owner.PlanHistoryJson),
                    run.Id, binding.Revision, ownership, ct)
                : null;
            bool Workset(string? json, string field) => allocation == null ? Refers(json) : Refers(json,
                allocationContract: document => ArtifactAcquisitionAllocationReferences.WorksetValues(document, field, allocation, ct));
            if (Workset(run.PayloadJson, "payload") || Workset(run.CurrentItemJson, "currentItem") || Workset(run.WaitPromptJson, "waitPrompt") ||
                Refers(run.PlanSnapshotJson, frozenWorkflowPlan: true) || Refers(run.PendingSignalJson)) return Found($"run:{run.Id}");
        }
        var resources = await ReadAsync("resources", () => db.ResourcesV2.AsNoTracking().Where(r => r.Path != null).Select(r => r.Path!).ToListAsync(ct), ct);
        var marks = await ReadAsync("marks", () => db.PathMarks.AsNoTracking().Where(m => !m.IsDeleted).Select(m => new {m.Path, m.ConfigJson}).ToListAsync(ct), ct);
        if (resources.Any(p => RefersPath(p)) || marks.Any(m => RefersPath(m.Path) || Refers(m.ConfigJson, true))) return Found("resource-or-path-mark");
        var libraries = await ReadAsync("libraries", () => db.MediaLibrariesV2.AsNoTracking().Select(l => l.Paths).ToListAsync(ct), ct);
        if (libraries.SelectMany(MediaLibraryV2Extensions.ParsePaths).Any(p => RefersPath(p))) return Found("media-library");

        var downloads = await ReadAsync("downloads", () => db.DownloadTasks.AsNoTracking().Select(d => new {d.Id, d.DownloadPath, d.Status, d.Options, d.ThirdPartyId}).ToListAsync(ct), ct);
        var results = await ReadAsync("results", () => db.DownloadResults.AsNoTracking().Select(d => new {d.DownloadTaskId, d.Path, d.FilesJson}).ToListAsync(ct), ct);
        var files = await ReadAsync("files", () => db.DownloadTaskFiles.AsNoTracking().Select(d => new {d.DownloadTaskId, d.Path}).ToListAsync(ct), ct);
        int? ownDownload = null;
        if (privatePaths == null && source.Kind == "download" && DownloadArtifactCoordinator.ReadMetadata(source)?.DownloadTaskId is { } downloadId &&
            downloads.SingleOrDefault(d => d.Id == downloadId)?.Status == DownloadTaskDbModelStatus.Complete)
        {
            var operations = await ReadAsync("transport:" + downloadId, () => db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == downloadId).ToListAsync(ct), ct);
            var exact = source.Files.Select(Relocate).ToHashSet(ArtifactManifestFiles.PathComparer);
            var recorded = results.Where(r => r.DownloadTaskId == downloadId).ToArray();
            // The one exception is this exact completed transport's own historical receipt.
            // Any unrelated transport, unbound task or extra registered file remains a reference.
            // Dispatch records stop at "started"; native task completion and the exact
            // result receipt above/below prove completion. Never rewrite that journal or
            // treat queued/starting/unknown outcomes as a completed transport.
            if (operations.Count > 0 && operations.All(o => owners.Contains(o.WorkflowRunId) && o.Kind == "download" &&
                    o.State is "started" or "completed") && recorded.Length > 0 &&
                exact.SetEquals(recorded.SelectMany(r => JsonSerializer.Deserialize<string[]>(r.FilesJson) ?? []).Select(Relocate)) &&
                files.Where(f => f.DownloadTaskId == downloadId).All(f => exact.Contains(Relocate(f.Path)))) ownDownload = downloadId;
        }
        // A completed native download may release its historical receipts. For an unfinished
        // task, only explicit, quiesced task deletion can release a failed/disabled native
        // writer, and its persisted dispatch and concrete destination must still match.
        var allowStoppedDownload = explicitTaskDirectoryCleanup && abandonUnfinishedFiles;
        bool ReleasableNativeState(DownloadTaskDbModelStatus status) => status == DownloadTaskDbModelStatus.Complete ||
            allowStoppedDownload && status is DownloadTaskDbModelStatus.Failed or DownloadTaskDbModelStatus.Disabled;
        bool MatchesStoppedDispatch(string? options, string? journal, string id, string path)
        {
            if (string.IsNullOrEmpty(options) || string.IsNullOrEmpty(journal)) return false;
            try
            {
                using var native = JsonDocument.Parse(options); using var recorded = JsonDocument.Parse(journal);
                var request = native.RootElement.Deserialize<CloudDownloadRequest>(JsonSerializerOptions.Web);
                return request?.OperationId == id && !string.IsNullOrWhiteSpace(request.Source?.Id) &&
                    !string.IsNullOrWhiteSpace(request.Source.Url) && JsonElement.DeepEquals(native.RootElement, recorded.RootElement) &&
                    Same(Relocate(CloudDownloadDestinations.ExecutionDirectory(request)), Relocate(path));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or IOException)
            { return false; }
        }
        if (directoryDownloadTaskId is { } stageDownload)
        {
            var ownedTask = downloads.SingleOrDefault(d => d.Id == stageDownload);
            var transports = await db.CloudDownloadOperations.AsNoTracking()
                .Where(o => o.DownloadTaskId == stageDownload).ToListAsync(ct);
            if (directoryOwnerTaskId == null || privatePaths != null || ownedTask == null ||
                !ReleasableNativeState(ownedTask.Status) ||
                !Same(Relocate(ownedTask.DownloadPath), Relocate(source.RootDirectory)) ||
                transports.Count == 0 || transports.Any(o => o.Kind != "download" || !owners.Contains(o.WorkflowRunId) ||
                    o.State is not ("started" or "completed") || ownedTask.Status != DownloadTaskDbModelStatus.Complete &&
                    !MatchesStoppedDispatch(ownedTask.Options, o.RequestJson, o.Id, ownedTask.DownloadPath)) ||
                await db.DownloadResultOwners.AnyAsync(o => o.DownloadTaskId == stageDownload &&
                    (o.AcquisitionTaskId != directoryOwnerTaskId.Value || !owners.Contains(o.WorkflowRunId)), ct))
                return Found("invalid-stage-download-owner");
            ownDownload = stageDownload;
        }
        var privateTransport = await OwnedWorkspaceTransportAsync(source, owners, roots, ct);
        var releasedDownloads = ownDownload is { } released ? new HashSet<int> {released} : [];
        if (explicitTaskDirectoryCleanup)
        {
            var releasable = downloads.Where(d => ReleasableNativeState(d.Status)).ToDictionary(d => d.Id);
            var ownedIds = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId != null && owners.Contains(o.WorkflowRunId))
                .Select(o => o.DownloadTaskId!.Value).Distinct().ToArrayAsync(ct);
            var ids = ownedIds.Where(releasable.ContainsKey).ToArray();
            var stoppedIds = ids.Where(id => releasable[id].Status != DownloadTaskDbModelStatus.Complete).ToArray();
            var transports = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId != null && ids.Contains(o.DownloadTaskId.Value))
                .Select(o => new {Id = o.DownloadTaskId!.Value, OperationId = o.Id, o.WorkflowRunId, o.Kind, o.State,
                    RequestJson = stoppedIds.Contains(o.DownloadTaskId.Value) ? o.RequestJson : null}).ToListAsync(ct);
            var resultOwners = await db.DownloadResultOwners.AsNoTracking().Where(o => ids.Contains(o.DownloadTaskId))
                .Select(o => new {o.DownloadTaskId, o.AcquisitionTaskId, o.WorkflowRunId}).ToListAsync(ct);
            foreach (var group in transports.GroupBy(o => o.Id))
            {
                var native = releasable[group.Key];
                if (group.All(o => owners.Contains(o.WorkflowRunId) && o.Kind == "download" && o.State is "started" or "completed" &&
                    (native.Status == DownloadTaskDbModelStatus.Complete ||
                     MatchesStoppedDispatch(native.Options, o.RequestJson, o.OperationId, native.DownloadPath))) &&
                    resultOwners.Where(o => o.DownloadTaskId == group.Key).All(o =>
                        o.AcquisitionTaskId == directoryOwnerTaskId && owners.Contains(o.WorkflowRunId)))
                    releasedDownloads.Add(group.Key);
            }
        }
        var stageDownloads = new Dictionary<int, string>();
        if (processingPartId != null && directoryOwnerRunId != null && directoryOwnerTaskId != null && privatePaths == null)
        {
            stageDownloads = await db.CloudDownloadOperations.AsNoTracking()
                .Where(o => owners.Contains(o.WorkflowRunId) && o.TargetId == processingPartId && o.Kind == "download" &&
                    (o.State == "started" || o.State == "completed") && o.DownloadTaskId.HasValue &&
                    !db.CloudDownloadOperations.Any(other => other.DownloadTaskId == o.DownloadTaskId && other.Id != o.Id) &&
                    !db.DownloadResultOwners.Any(r => r.DownloadTaskId == o.DownloadTaskId &&
                        (r.AcquisitionTaskId != directoryOwnerTaskId || !owners.Contains(r.WorkflowRunId))))
                .ToDictionaryAsync(o => o.DownloadTaskId!.Value, o => o.Id, ct);
        }
        if (downloads.Any(d => !releasedDownloads.Contains(d.Id) && (RefersPath(d.DownloadPath, d.Id != privateTransport?.Id) || RefersCloudRequest(d.Options,
                providerId: CloudSites.Sources.FirstOrDefault(s => s.ThirdPartyId == d.ThirdPartyId)?.ProviderId, outputDirectory: d.DownloadPath,
                stageDownloadOperation: d.Status == DownloadTaskDbModelStatus.Complete ? stageDownloads.GetValueOrDefault(d.Id) : null,
                privateAllocations: d.Id == privateTransport?.Id
                    ? [privateTransport.Value.Directory, privateTransport.Value.DownloadDirectory] : null)))) return Found("download-task");
        if (files.Any(f => !releasedDownloads.Contains(f.DownloadTaskId) && RefersPath(f.Path))) return Found("download-file");
        if (results.Any(r => !releasedDownloads.Contains(r.DownloadTaskId) && (RefersPath(r.Path, r.DownloadTaskId != privateTransport?.Id) || Refers(r.FilesJson, true)))) return Found("download-result");
        var otherDownloads = await ReadAsync("other-transports:" + ScopeKey(owners), () => db.CloudDownloadOperations.AsNoTracking().Where(o => !owners.Contains(o.WorkflowRunId))
            .Select(o => new {o.Id, o.Kind, o.RequestJson, o.ReceiptJson}).ToListAsync(ct), ct);
        return otherDownloads.Any(o => (o.Kind == "download" ? RefersCloudRequest(o.RequestJson, operationId: o.Id) : Refers(o.RequestJson, true)) ||
            Refers(o.ReceiptJson, true)) && Found("cloud-operation");
    }
    // A completed download may also be the parent directory of a later, private processing
    // attempt. Its exact result files stay protected. Release only its container's ancestor
    // role, and only when the locator, input lineage and transport all agree on ownership.
    private async Task<(int Id, string Directory, string DownloadDirectory)?> OwnedWorkspaceTransportAsync(ArtifactRecord source,
        IReadOnlyList<int> owners, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        try { return await OwnedWorkspaceTransportCoreAsync(source, owners, candidates, ct); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            // A missing historical workspace disproves this optional container exemption.
            // Keep all ordinary path references; it must not abort unrelated file previews.
            return null;
        }
    }

    private async Task<(int Id, string Directory, string DownloadDirectory)?> OwnedWorkspaceTransportCoreAsync(ArtifactRecord source,
        IReadOnlyList<int> owners, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        if (workspaces == null || owners.Count == 0 || candidates.Count == 0 ||
            source is not {Managed: true, Kind: "processedFiles", ParentArtifactIds.Count: 1} ||
            source.OperationId.Length != 64 || !source.OperationId.All(Uri.IsHexDigit)) return null;
        var operation = await db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == source.OperationId, ct);
        if (operation is not {State: ArtifactOperationState.Completed, Attempt: > 0} || operation.OutputArtifactId != source.Id) return null;
        var parentId = source.ParentArtifactIds[0];
        var input = await OwnedDownloadLineageAsync(source, operation, owners, ct);
        if (input == null) return null;
        var id = input.TransportId;
        var processing = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(source.MetadataJson ?? "null", JsonSerializerOptions.Web);
        FileProcessingWorkspaceResolver.Location? location;
        if (processing?.CompletedThroughStepId != null)
        {
            if (processing.InputArtifactId != parentId) return null;
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(processing.PlanJson, JsonSerializerOptions.Web);
            if (plan == null || !plan.Steps.Any(step => step.Id == processing.CompletedThroughStepId)) return null;
            var key = FileArtifactProcessor.CheckpointKey(operation.InputFingerprint, plan);
            location = await workspaces.InspectCompletedCheckpointAsync(key, operation.Id, operation.Attempt,
                processing.WorkspaceDirectoryScope?.Directory ?? processing.StoragePolicy?.ProcessingWorkspaceDirectory, ct);
        }
        else location = await workspaces.InspectExistingAsync("operations", operation.Id, operation.Attempt, ct);
        if (location == null || !ArtifactCleanupPathReferences.StrictlyUnder(location.Directory, location.StorageRoot) ||
            !ArtifactCleanupPathReferences.StrictlyUnder(source.RootDirectory, location.Directory) ||
            candidates.Any(p => !ArtifactCleanupPathReferences.StrictlyUnder(p, location.Directory))) return null;
        var originalInputRoot = await workspaces.ReadOriginalArtifactDirectoryAsync(input.Artifact, ct);
        if (originalInputRoot == null || !Same(originalInputRoot, location.StorageRoot)) return null;
        var download = await db.DownloadTasks.AsNoTracking().Where(d => d.Id == id && d.Status == DownloadTaskDbModelStatus.Complete)
            .Select(d => d.DownloadPath).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(download)) return null;
        var downloadDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaces.Relocate(download)));
        var resultDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.StorageRoot));
        // A provider can publish the completed file set inside its native task's container.
        // The exact result receipt below proves the inner root; the native task proves the
        // outer root. Only these two container roles are released, never receipt file paths.
        if (!Same(downloadDirectory, resultDirectory) &&
            !ArtifactCleanupPathReferences.StrictlyUnder(resultDirectory, downloadDirectory)) return null;
        var dispatches = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == id)
            .Select(o => new {o.WorkflowRunId, o.Kind, o.State}).ToListAsync(ct);
        if (dispatches.Count == 0 || dispatches.Any(o => !owners.Contains(o.WorkflowRunId) || o.Kind != "download" ||
            o.State is not ("started" or "completed")) || await db.DownloadResultOwners.AnyAsync(o =>
            o.DownloadTaskId == id && !owners.Contains(o.WorkflowRunId), ct)) return null;
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(input.Artifact.ManifestJson, JsonSerializerOptions.Web);
        if (manifest is not {Entries.Count: > 0}) return null;
        var inputFiles = ArtifactManifestFiles.ResolveFiles(location.StorageRoot, manifest).ToHashSet(ArtifactManifestFiles.PathComparer);
        var receipts = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == id).Select(r => new {r.Path,r.FilesJson}).ToListAsync(ct);
        var entries = await db.DownloadTaskFiles.AsNoTracking().Where(f => f.DownloadTaskId == id).Select(f => f.Path).ToListAsync(ct);
        if (receipts.Count == 0 || receipts.Any(r => !Same(workspaces.Relocate(r.Path), location.StorageRoot)) ||
            !inputFiles.SetEquals(receipts.SelectMany(r => JsonSerializer.Deserialize<string[]>(r.FilesJson) ?? []).Select(workspaces.Relocate)) ||
            entries.Any(p => !inputFiles.Contains(workspaces.Relocate(p)))) return null;
        return (id, resultDirectory, downloadDirectory);
    }

    private sealed record TransportInput(int TransportId, ArtifactDbModel Artifact);

    private async Task<TransportInput?> OwnedDownloadLineageAsync(ArtifactRecord source, ArtifactOperationDbModel operation,
        IReadOnlyList<int> owners, CancellationToken ct)
    {
        var operations = new HashSet<string>(StringComparer.Ordinal) {operation.Id};
        var parentId = source.ParentArtifactIds.Single();
        var expectedFingerprint = operation.InputFingerprint;
        // Each edge must agree with the saved recipe, completed producer and immutable input fingerprint.
        // A branch, publication, cycle or foreign owner leaves the container protected.
        while (operations.Count <= 64)
        {
            var parent = await (from a in db.Set<ArtifactDbModel>().AsNoTracking()
                join op in db.Set<ArtifactOperationDbModel>().AsNoTracking() on a.OperationId equals op.Id
                where a.Id == parentId && a.Managed && a.Fingerprint == expectedFingerprint &&
                    op.State == ArtifactOperationState.Completed && op.OutputArtifactId == a.Id
                select new {a.OperationId, a.Kind, a.MetadataJson, a.ManifestJson, op.InputFingerprint, Producer = op.Kind}).SingleOrDefaultAsync(ct);
            if (parent?.MetadataJson == null || !operations.Add(parent.OperationId)) return null;
            if (parent.Kind == "download" && parent.Producer == "download")
            {
                var metadata = JsonSerializer.Deserialize<DownloadArtifactMetadata>(parent.MetadataJson, JsonSerializerOptions.Web);
                if (metadata?.DownloadTaskId is not { } transportId) return null;
                var ownerKeys = owners.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
                var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b => operations.Contains(b.OperationId))
                    .Select(b => new {b.OperationId, b.OwnerKind, b.OwnerId}).ToListAsync(ct);
                if (bindings.Select(b => b.OperationId).Distinct().Count() != operations.Count ||
                    bindings.Any(b => b.OwnerKind != "workflow" || !ownerKeys.Contains(b.OwnerId))) return null;
                var artifact = await db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == parentId, ct);
                if (artifact.OperationId != parent.OperationId || artifact.Fingerprint != expectedFingerprint ||
                    artifact.Kind != "download" || !artifact.Managed) return null;
                return new(transportId, artifact);
            }
            if (parent.Kind != "processedFiles" || parent.Producer != "files.process") return null;
            var step = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(parent.MetadataJson, JsonSerializerOptions.Web);
            var parents = await db.Set<ArtifactLineageDbModel>().AsNoTracking().Where(l => l.ChildArtifactId == parentId)
                .Select(l => l.ParentArtifactId).Take(2).ToListAsync(ct);
            if (parents.Count != 1 || step?.InputArtifactId != parents[0]) return null;
            parentId = parents[0];
            expectedFingerprint = parent.InputFingerprint;
        }
        return null;
    }

    private static bool Same(string left, string right) => ArtifactManifestFiles.PathComparer.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)));
}
