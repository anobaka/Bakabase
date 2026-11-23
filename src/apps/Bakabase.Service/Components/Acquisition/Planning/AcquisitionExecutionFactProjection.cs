using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A fact is tied to a part and the latest invocation at the current cursor. Task
/// facets never combine the local input of one part with another part's storage failure.
/// A task-level capacity failure (the empty-part scope added by <see cref="WithTaskSpaceFailure"/>)
/// is intentionally allowed to combine with a task-level downloaded-pending fact.</summary>
internal sealed record AcquisitionExecutionFactScope(string PartId, string NodeId, int Attempt,
    bool LocalInput, bool SpaceInsufficient);
internal sealed record AcquisitionExecutionFacts(bool AllRequiredInputsLocal,
    IReadOnlyList<AcquisitionExecutionFactScope> Scopes)
{
    internal string? FileCleanupFact { get; init; }
    internal static readonly AcquisitionExecutionFacts Unknown = new(false, []);
    internal IReadOnlyList<string> Tags => AcquisitionExecutionFactProjection.FactTags.Where(MatchesOne).ToArray();
    private bool MatchesOne(string value) => value switch
    {
        "downloadedPending" => AllRequiredInputsLocal && Scopes.Any(s => s.LocalInput),
        "spaceInsufficient" => Scopes.Any(s => s.SpaceInsufficient),
        "awaitingFileRelease" or "fileCleanupNeedsReview" => FileCleanupFact == value,
        _ => false,
    };
    internal bool Matches(IReadOnlyCollection<string> filters)
    {
        if (!filters.All(MatchesOne)) return false;
        if (!filters.Contains("downloadedPending") || !filters.Contains("spaceInsufficient")) return true;
        // Keep the multi-part guard: two unrelated part scopes must not satisfy a
        // combined filter. The synthetic empty-part scope represents a capacity
        // failure reported for the whole task and is safe to combine with local input.
        return Scopes.Any(s => s.LocalInput && s.SpaceInsufficient) ||
            Scopes.Any(s => string.IsNullOrEmpty(s.PartId) && s.SpaceInsufficient);
    }
    internal AcquisitionExecutionFacts WithTaskSpaceFailure() => this with
    {Scopes = Scopes.Append(new("", "", 0, false, true)).ToArray()};
}

/// <summary>Local input facts from recorded artifacts and the current download snapshot, without
/// filesystem inspection. SQLite reduces bounded private checkpoints/manifests before returning compact rows.</summary>
public sealed partial class AcquisitionExecutionFactProjection(BakabaseDbContext db, IArtifactOperationStore? store = null)
{
    public static readonly string[] FactTags = ["downloadedPending", "spaceInsufficient", "awaitingFileRelease", "fileCleanupNeedsReview"];
    internal async Task<Dictionary<int, AcquisitionExecutionFacts>> ReadAsync(IReadOnlyCollection<int> ids, CancellationToken ct,
        IReadOnlyDictionary<int, AcquisitionDownloadActivity>? downloads = null, IReadOnlyCollection<int>? preparedIds = null)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => AcquisitionExecutionFacts.Unknown);
        if (store != null)
            foreach (var batch in (preparedIds ?? ids).Chunk(50))
                await ReadPreparedFactsAsync(batch, result, store, ct);
        foreach (var batch in result.Keys.ToArray().Chunk(50))
        {
            var current = await db.Database.SqlQueryRaw<CurrentFact>(CurrentSql, JsonSerializer.Serialize(batch),
                typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name).ToListAsync(ct);
            await PreserveDirectoryOnlyRunFactsAsync(current, ct);
            var runIds = current.Select(r => r.RunId).ToArray();
            var owners = runIds.Select(r => r.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in current)
                try
                {
                    foreach (var target in Read<TargetFact[]>(row.TargetsJson) ?? [])
                        foreach (var operation in new[] {target.DownloadOperationId, target.ProcessingOperationId, target.PublicationOperationId})
                            if (operation != null) desired.Add(operation);
                    foreach (var node in Read<NodeFact[]>(row.NodesJson) ?? [])
                        if (node.ContinueOperationId != null) desired.Add(node.ContinueOperationId);
                }
                catch (JsonException) { /* This task remains unknown below. */ }
            var wanted = desired.ToArray();
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OwnerKind == "workflow" &&
                owners.Contains(b.OwnerId) && wanted.Contains(b.OperationId))
                .Select(b => new Binding(b.OwnerId, b.Slot, b.OperationId)).Take(51201).ToListAsync(ct);
            if (bindings.Count > 51200) continue;
            var boundIds = bindings.Select(b => b.OperationId).Distinct().ToArray();
            var artifacts = new List<ArtifactFact>();
            foreach (var chunk in boundIds.Chunk(100))
                artifacts.AddRange(await db.Database.SqlQueryRaw<ArtifactFact>(ArtifactSql, JsonSerializer.Serialize(chunk)).ToListAsync(ct));
            foreach (var row in current)
            {
                var ownBindings = bindings.Where(b => b.Owner == row.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (ownBindings.Length > 1024) continue;
                try {result[row.TaskId] = Project(row, ownBindings, artifacts, downloads?.GetValueOrDefault(row.TaskId)?.CompletedPartIds);}
                catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or IOException)
                { /* Missing/inconsistent historical evidence remains unknown. */ }
            }
        }
        var cleanups = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id) &&
                t.DeletedAt == null && t.Status == AcquisitionStatus.Completed &&
                (t.DeletedCleanupReasons == DeferredStageCleanup.AwaitRemoval || t.DeletedCleanupReasons == DeferredStageCleanup.RequiresReview))
            .Select(t => new {t.Id, t.DeletedCleanupReasons, t.DeletedIntermediateCleanup, t.DeletedDeliveryCleanup}).ToListAsync(ct);
        foreach (var task in cleanups)
        {
            var fact = task.DeletedCleanupReasons switch
            {
                DeferredStageCleanup.AwaitRemoval when DeferredStageCleanup.HasPendingRemovalReceipts(
                    task.DeletedIntermediateCleanup, task.DeletedDeliveryCleanup) => "awaitingFileRelease",
                DeferredStageCleanup.RequiresReview when task.DeletedIntermediateCleanup != null && task.DeletedDeliveryCleanup != null
                    => "fileCleanupNeedsReview",
                _ => null
            };
            result[task.Id] = result[task.Id] with {FileCleanupFact = fact};
        }
        return result;
    }

    // A ready local-only revision has no current workflow. Its inputs remain registered on
    // earlier runs. Search uses the same database ownership/manifest checks as continuation;
    // only execution opens the files. A completed download for just one part is insufficient.
    private async Task ReadPreparedFactsAsync(int[] ids, Dictionary<int, AcquisitionExecutionFacts> result,
        IArtifactOperationStore artifactStore, CancellationToken ct)
    {
        var candidates = await db.AcquisitionTasks.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.DeletedAt == null && t.WorkflowRunId == null &&
                t.Phase == "ready" && t.Status == AcquisitionStatus.Waiting && t.BuilderId != null && t.PlanJson != null)
            .Select(t => new AcquisitionTaskDbModel {Id = t.Id, Phase = t.Phase, Status = t.Status, BuilderId = t.BuilderId,
                PlanJson = t.PlanJson, BuilderStateJson = t.BuilderStateJson, PlanRevision = t.PlanRevision})
            .ToListAsync(ct);
        foreach (var task in candidates)
        {
            try
            {
                var registered = await AcquisitionPlanService.RegisteredPreparedInputsAsync(db, artifactStore, task, ct);
                if (registered.Count > 0)
                    result[task.Id] = new(true, registered.Select(p => new AcquisitionExecutionFactScope(p.PartId, "", 0, true, false)).ToArray());
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or JsonException)
            { /* Stale registrations and invalid old plans cannot supply a positive fact. */ }
        }
    }

    private static AcquisitionExecutionFacts Project(CurrentFact row, IReadOnlyList<Binding> bindings,
        IReadOnlyList<ArtifactFact> artifacts, IReadOnlySet<string>? completedDownloads)
    {
        if (row.RunStatus is not ((int)WorkflowRunStatus.Waiting or (int)WorkflowRunStatus.Failed or (int)WorkflowRunStatus.Interrupted) ||
            row.HasPendingSignal != 0 || row.NodeStatus is not ("waiting" or "failed") || row.NodeKind == null ||
            row.NodeId == null || row.NodeAttempt < 1 || row.TriggerKind != CloudWorkflow.Trigger || row.OwnsRun != 1)
            return AcquisitionExecutionFacts.Unknown;
        var planIds = Read<string?[]>(row.PartIdsJson) ?? [];
        var targets = Read<TargetFact[]>(row.TargetsJson) ?? [];
        var nodes = Read<NodeFact[]>(row.NodesJson) ?? [];
        if (planIds.Length is 0 or > 100 || planIds.Any(string.IsNullOrWhiteSpace) || planIds.Distinct().Count() != planIds.Length ||
            targets.Length != planIds.Length || targets.Any(t => !t.Valid) || targets.Select(t => t.Id).Distinct().Count() != targets.Length ||
            !planIds.ToHashSet().SetEquals(targets.Select(t => t.Id)) || nodes.Length == 0 ||
            nodes.Length != row.RemainingNodes || nodes.Any(n => !n.ConfigValid || string.IsNullOrWhiteSpace(n.NodeId)) ||
            nodes[0].Kind != row.NodeKind || nodes[0].NodeId != row.NodeId)
            return AcquisitionExecutionFacts.Unknown;
        // Completed/excluded parts cannot lend a false positive to a pending sibling.
        if (targets.Any(t => !t.Excluded && (t.PublishedArtifactId != null || t.PublishedCount > 0 ||
                t.PublicationStatus is "completed" or "reused") &&
            !ReopensPublication(t, nodes) && !HasDeliveredOutput(t, bindings, artifacts)))
            return AcquisitionExecutionFacts.Unknown;
        var pending = targets.Where(t => !t.Excluded &&
            (ReopensPublication(t, nodes) || !HasDeliveredOutput(t, bindings, artifacts))).ToArray();
        if (pending.Length == 0) return AcquisitionExecutionFacts.Unknown;
        var localRemainder = nodes.Select((n, index) => (n, index)).All(x =>
            x.n.Kind is AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts" or "download.cleanupArtifacts" ||
            x.index == 0 && x.n.Kind == "download.awaitCompletion");
        var inputs = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var part in pending)
        {
            var consumer = nodes.FirstOrDefault(n => Consumes(n, part.Id));
            inputs[part.Id] = consumer != null && HasInput(consumer, part, bindings, artifacts, completedDownloads);
        }
        var allLocal = localRemainder && pending.All(p => inputs.GetValueOrDefault(p.Id));
        var scopes = new List<AcquisitionExecutionFactScope>();
        foreach (var target in pending)
        {
            if (!Consumes(nodes[0], target.Id)) continue;
            var local = localRemainder && HasInput(nodes[0], target, bindings, artifacts, completedDownloads);
            var space = SpaceAtCurrentScope(row, nodes[0], target);
            scopes.Add(new(target.Id, row.NodeId, row.NodeAttempt, local, space));
        }
        return new(allLocal, scopes);
    }

    private static bool ReopensPublication(TargetFact target, IReadOnlyList<NodeFact> nodes) =>
        nodes.Any(n => n.TargetId == target.Id &&
            (n.Kind == AssessFileArtifactDeliveryActivity.ActivityKind && !target.ReviewAccepted &&
                target.ReviewArtifactId == target.ProcessedArtifactId && target.ReviewArtifactId != null ||
             n.Kind == AcquisitionPlanProcessingActivity.ActivityKind && n.ContinueArtifactId == target.PublishedArtifactId &&
                n.ContinueArtifactId != null));
    private static bool HasDeliveredOutput(TargetFact target, IReadOnlyList<Binding> bindings, IReadOnlyList<ArtifactFact> artifacts) =>
        target.PublishedArtifactId != null && target.PublicationOperationId != null &&
        artifacts.Any(a => a.Id == target.PublishedArtifactId && a.OperationId == target.PublicationOperationId &&
            a.Valid == 1 && a.Kind == "publishedFiles" && a.OperationKind == "files.publish") &&
        bindings.Any(b => b.OperationId == target.PublicationOperationId && b.Slot == target.Id + ":artifact.publish");

    private static bool Consumes(NodeFact node, string partId) => node.Kind switch
    {
        AcquisitionPlanProcessingActivity.ActivityKind or AssessFileArtifactDeliveryActivity.ActivityKind => node.TargetId == partId,
        "fs.publishArtifacts" or "download.awaitCompletion" => true,
        _ => false,
    };
    private static bool HasInput(NodeFact node, TargetFact target, IReadOnlyList<Binding> bindings,
        IReadOnlyList<ArtifactFact> artifacts, IReadOnlySet<string>? completedDownloads)
    {
        if (target.AcquisitionNonce != null) return false;
        if (node.Kind == "fs.publishArtifacts" && node.Source is not (null or "processed" or "download")) return false;
        string? artifactId, operationId, fingerprint = null; string role;
        if (node.Kind == AcquisitionPlanProcessingActivity.ActivityKind && node.ContinueArtifactId != null)
        {
            artifactId = node.ContinueArtifactId; operationId = node.ContinueOperationId; fingerprint = node.ContinueFingerprint;
            role = "continuation";
        }
        else if (node.Kind == AssessFileArtifactDeliveryActivity.ActivityKind || node.Kind == "fs.publishArtifacts" && node.Source != "download")
        {artifactId = target.ProcessedArtifactId; operationId = target.ProcessingOperationId; role = "processed";}
        else {artifactId = target.DownloadArtifactId; operationId = target.DownloadOperationId; role = "download";}
        if (node.Kind == AssessFileArtifactDeliveryActivity.ActivityKind && target.ReviewArtifactId != null)
        {
            if (target.ReviewArtifactId != artifactId || string.IsNullOrWhiteSpace(target.ReviewFingerprint)) return false;
            fingerprint = target.ReviewFingerprint;
        }
        if (artifactId != null)
        {
            var artifact = artifacts.SingleOrDefault(a => a.Id == artifactId && a.OperationId == operationId && a.Valid == 1);
            if (artifact == null || fingerprint != null && fingerprint != artifact.Fingerprint ||
                !SupportedArtifact(artifact) || role == "continuation" && string.IsNullOrWhiteSpace(fingerprint)) return false;
            var slots = bindings.Where(b => b.OperationId == artifact.OperationId).Select(b => b.Slot).ToArray();
            return role switch
            {
                "continuation" => slots.Contains(target.Id + ":artifact.continuation"),
                "download" => artifact.Kind == "download" && artifact.OperationKind == "download" && slots.Contains(target.Id + ":artifact.download"),
                // Current-directory captures are immutable local inputs, not newly
                // processed artifacts. Their exact continuation binding proves this
                // run/part adopted them before assessment or direct publication.
                "processed" => artifact.Kind == AcquisitionCurrentDirectoryFiles.Kind
                    ? slots.Contains(target.Id + ":artifact.continuation")
                    : artifact.Kind is "processedFiles" or "publishedFiles" &&
                      (slots.Contains(target.Id + ":artifact.process") || slots.Any(s => s.StartsWith(target.Id + ":process:", StringComparison.Ordinal)) ||
                       // Assessment can execute another processing round before pausing.
                       // Only its exact successful output binding is local result evidence.
                       artifact.Kind == "processedFiles" && slots.Contains(target.Id + ":adaptive:output:" + artifact.Id)),
                _ => false,
            };
        }
        // The downloader may have finished since the workflow last waited. Only its bound
        // await step can use this observation; continuation still verifies the actual files.
        return node.Kind == "download.awaitCompletion" && target.DownloadOperationId == null &&
            completedDownloads?.Contains(target.Id) == true;
    }

    private static bool SupportedArtifact(ArtifactFact artifact) => artifact.Kind switch
    {
        "download" => artifact.OperationKind == "download",
        "processedFiles" => artifact.OperationKind == "files.process",
        "publishedFiles" => artifact.OperationKind == "files.publish",
        AcquisitionCurrentDirectoryFiles.Kind => artifact.OperationKind == AcquisitionCurrentDirectoryFiles.OperationKind,
        _ => false,
    };

    private static bool SpaceAtCurrentScope(CurrentFact row, NodeFact node, TargetFact target)
    {
        // A successfully resumed attempt, stale prompt or a warning for another part cannot
        // prove this invocation remains blocked. Prompt.targetId is the shared scope boundary.
        if (row.RunStatus != (int)WorkflowRunStatus.Waiting || row.NodeStatus != "waiting" || row.WaitReason != row.NodeWaitReason)
            return false;
        var prompt = Read<PromptFact>(row.PromptJson);
        if (prompt?.TargetId != target.Id) return false;
        var typed = node.Kind switch
        {
            AcquisitionPlanProcessingActivity.ActivityKind => row.WaitReason == "cloud.fileProcessing" &&
                node.TargetId == target.Id && target.ProcessingWaitReason == prompt.Reason,
            AssessFileArtifactDeliveryActivity.ActivityKind => row.WaitReason == "cloud.fileProcessing" && prompt.DeliveryReview == true &&
                node.TargetId == target.Id && target.ReviewArtifactId == prompt.ArtifactId &&
                target.ReviewFingerprint == prompt.Fingerprint && target.ReviewWaitReason == prompt.Reason &&
                target.ProcessedArtifactId == prompt.ArtifactId && !string.IsNullOrWhiteSpace(prompt.Fingerprint),
            "fs.publishArtifacts" => row.WaitReason == "artifacts.publication" && target.PublicationWaitReason == prompt.Reason,
            _ => false,
        };
        if (!typed) return false;
        return ExecutionProblemClassifier.IsSpaceInsufficient(prompt.Reason) ||
            prompt.Reason == "StorageUnavailable" && WorkflowSourceRunProblems.Extract(prompt.Message).Any(ExecutionProblemClassifier.IsSpaceInsufficient);
    }
    private static T? Read<T>(string? json) where T : class => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, WorkflowJson.Options);

    private sealed record Binding(string Owner, string Slot, string OperationId);
    internal sealed record NodeFact(string? NodeId, string? Kind, bool ConfigValid, string? TargetId, string? Source,
        string? ContinueArtifactId, string? ContinueOperationId, string? ContinueFingerprint);
    internal sealed record TargetFact(string Id, bool Valid, bool Excluded,
        string? AcquisitionNonce, string? DownloadArtifactId, string? DownloadOperationId, string? ProcessedArtifactId,
        string? ProcessingOperationId, string? PublishedArtifactId, string? PublicationOperationId, int PublishedCount, string? PublicationStatus,
        string? ProcessingWaitReason, string? PublicationWaitReason,
        string? ReviewArtifactId, string? ReviewFingerprint, string? ReviewWaitReason, bool ReviewAccepted);
    private sealed record PromptFact(string? TargetId, string? Reason, string? Message, string? ArtifactId,
        string? Fingerprint, bool? DeliveryReview);
    public sealed class CurrentFact
    {
        public int TaskId { get; set; } public int RunId { get; set; } public int RunStatus { get; set; }
        public int OwnsRun { get; set; } public int HasPendingSignal { get; set; }
        public int RemainingNodes { get; set; }
        public string? TriggerKind { get; set; } public string? WaitReason { get; set; }
        public string? NodeId { get; set; } public string? NodeKind { get; set; } public int NodeAttempt { get; set; }
        public string? NodeStatus { get; set; } public string? NodeWaitReason { get; set; } public string? PromptJson { get; set; }
        public string? PartIdsJson { get; set; } public string? TargetsJson { get; set; } public string? NodesJson { get; set; }
    }
    public sealed class ArtifactFact
    {
        public string Id { get; set; } = ""; public string OperationId { get; set; } = "";
        public string? Kind { get; set; } public string? OperationKind { get; set; } public string? Fingerprint { get; set; }
        public int Valid { get; set; }
    }
}
