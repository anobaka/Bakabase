using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionSupersededDeliveryInput(int ExpectedRevision, int ExpectedRunId,
    string PreviousArtifactId, string CurrentArtifactId);
public sealed record AcquisitionSupersededDeliveryResult(bool Retired, string Status, int FileCount = 0, long Bytes = 0, string? Message = null);

/// <summary>Retires only the previous publication explicitly borrowed by this part's successful revision.</summary>
public sealed class AcquisitionSupersededDeliveryService(BakabaseDbContext db, IArtifactOperationStore store,
    IUserStoragePolicy storage, IBOptions<AcquisitionOptions> options, AppService app,
    FileProcessingWorkspaceResolver? workspaces = null)
{
    private const string ReceiptKey = "supersededDeliveryRetirement";
    private sealed record Receipt(int TaskId, AcquisitionSupersededDeliveryInput Input, string StageId, bool Purging);

    public Task<AcquisitionSupersededDeliveryResult> RetireAsync(int taskId, AcquisitionSupersededDeliveryInput input,
        CancellationToken ct) => Execute(taskId, input, automatic: false, ct);

    public Task<AcquisitionSupersededDeliveryResult> RetireIfEnabledAsync(int taskId, AcquisitionSupersededDeliveryInput input,
        CancellationToken ct) => Execute(taskId, input, automatic: true, ct);

    internal async Task<AcquisitionSupersededDeliveryInput?> StartedInputAsync(int taskId, int revision, string previousArtifactId,
        CancellationToken ct)
    {
        var artifact = await store.GetArtifactAsync(previousArtifactId, ct);
        var receipt = ReadReceipt(artifact);
        return artifact?.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired && receipt?.TaskId == taskId &&
               receipt.Input.ExpectedRevision == revision && receipt.Input.PreviousArtifactId == previousArtifactId
            ? receipt.Input : null;
    }

    private static Receipt? ReadReceipt(ArtifactRecord? artifact) => artifact?.MetadataJson == null ? null :
        JsonNode.Parse(artifact.MetadataJson)?[ReceiptKey]?.Deserialize<Receipt>(WorkflowJson.Options);

    private async Task<AcquisitionSupersededDeliveryResult> Execute(int taskId, AcquisitionSupersededDeliveryInput input,
        bool automatic, CancellationToken ct)
    {
        if (taskId <= 0 || input.ExpectedRevision < 2 || input.ExpectedRunId <= 0 ||
            !Guid.TryParseExact(input.PreviousArtifactId, "N", out _) || !Guid.TryParseExact(input.CurrentArtifactId, "N", out _) ||
            input.PreviousArtifactId == input.CurrentArtifactId)
            throw new ArgumentException("Specify the two distinct publications and the completed acquisition revision.");
        var taskGate = AcquisitionTaskGate.For(taskId);
        if (!await taskGate.WaitAsync(0, ct)) return new(false, "busy");
        var runGate = WorkflowRunMutationGate.For(input.ExpectedRunId);
        try
        {
            if (!await runGate.WaitAsync(0, ct)) return new(false, "busy");
            try
            {
                var row = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
                if (row == null || row.DeletedAt != null) return new(false, "revision_changed");
                await using var fence = await store.AcquireMaintenanceAsync(ct);
                var prior = await store.GetArtifactAsync(input.PreviousArtifactId, ct);
                var current = await store.GetArtifactAsync(input.CurrentArtifactId, ct);
                var receipt = ReadReceipt(prior);
                if (receipt != null && (receipt.TaskId != taskId || receipt.Input != input || !Guid.TryParseExact(receipt.StageId, "N", out _)))
                    return new(false, "different_receipt");
                // A matching terminal receipt is an idempotent observation, including when
                // the worker crashed before recording success and newer revisions ran meanwhile.
                if (receipt != null && prior?.State == ArtifactLocationState.Retired) return Completed(prior);
                var sameRevision = row.Status == AcquisitionStatus.Completed && row.PlanRevision == input.ExpectedRevision &&
                                   row.WorkflowRunId == input.ExpectedRunId;
                // A committed receipt pins the original authorization. Later task revisions must
                // not strand a moved or partially purged directory, nor authorize a fresh retirement.
                if (!sameRevision && receipt == null) return new(false, "revision_changed");
                var plan = sameRevision && row.PlanJson != null
                    ? JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson, WorkflowJson.Options)
                    : row.PlanHistoryJson == null ? null : JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(
                        row.PlanHistoryJson, WorkflowJson.Options)?.SingleOrDefault(p => p.Revision == input.ExpectedRevision)?.Plan;
                var part = plan?.Parts.SingleOrDefault(p => p.ContinueFrom?.ArtifactId == input.PreviousArtifactId);
                if (part?.ContinueFrom is not { } previous || previous.TaskId != taskId || previous.PreviousRunId == input.ExpectedRunId ||
                    plan?.DeliveryLayout?.PartDirectories.TryGetValue(part.Id, out var currentRoot) != true)
                    return new(false, "unrelated_delivery");
                var runIds = new[] {previous.PreviousRunId, input.ExpectedRunId};
                if (await db.Set<WorkflowRunDbModel>().CountAsync(r => runIds.Contains(r.Id) &&
                    r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct) != 2) return new(false, "run_not_successful");
                foreach (var run in runIds)
                    if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, run, taskId, ct))
                        return new(false, "unrelated_delivery");
                if (prior is not {Kind: "publishedFiles"} || current is not {Kind: "publishedFiles", State: ArtifactLocationState.Available} ||
                    prior.Fingerprint != previous.Fingerprint || prior.OperationId != previous.OperationId ||
                    prior.Manifest.Entries.Count == 0 || !SamePath(current.RootDirectory, currentRoot!) ||
                    Overlaps(prior.RootDirectory, current.RootDirectory)) return new(false, "unrelated_delivery");
                var oldRun = previous.PreviousRunId.ToString(CultureInfo.InvariantCulture);
                var newRun = input.ExpectedRunId.ToString(CultureInfo.InvariantCulture);
                var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
                    .Where(b => b.OperationId == prior.OperationId || b.OperationId == current.OperationId).ToListAsync(ct);
                var slot = part.Id + ":artifact.publish";
                if (!bindings.Any(b => b.OwnerKind == "workflow" && b.OwnerId == oldRun && b.Slot == slot && b.OperationId == prior.OperationId) ||
                    !bindings.Any(b => b.OwnerKind == "workflow" && b.OwnerId == newRun && b.Slot == slot && b.OperationId == current.OperationId) ||
                    bindings.Any(b => b.OperationId == prior.OperationId && (b.OwnerKind != "workflow" ||
                        !(b.OwnerId == oldRun && b.Slot == slot || b.OwnerId == newRun && b.Slot == part.Id + ":artifact.continuation"))))
                    return new(false, "shared_or_unbound_delivery");
                foreach (var artifact in new[] {prior, current})
                {
                    var op = await store.GetOperationAsync(artifact.OperationId, ct);
                    if (op?.Kind != "files.publish" || op.State != ArtifactOperationState.Completed || op.OutputArtifactId != artifact.Id)
                        return new(false, "unrelated_delivery");
                }
                var metadata = JsonNode.Parse(prior.MetadataJson ?? "{}")!.AsObject();
                if (receipt == null && automatic && options.Value.KeepSupersededDeliveryFiles) return new(false, "keep");
                if (receipt == null ? prior.State != ArtifactLocationState.Available : prior.State != ArtifactLocationState.Quarantined)
                    return new(false, "source_unavailable");
                storage.EnsureTreeMutationAllowed(prior.RootDirectory);
                storage.EnsurePathAllowed(current.RootDirectory);
                var parent = Directory.GetParent(prior.RootDirectory)?.FullName ?? throw new InvalidDataException("A volume root cannot be retired.");
                receipt ??= new(taskId, input, Guid.NewGuid().ToString("N"), false);
                var stage = Path.Combine(parent, ".bakabase-retire-" + receipt.StageId);
                storage.EnsureTreeMutationAllowed(stage);
                if (await IsReferenced(taskId, prior, stage, sameRevision, ct)) return new(false, "referenced");
                if ((prior.Manifest.Directories?.Count ?? 0) > 0) return new(false, "unverified_directory_replacement");
                if (!await CoversPreviousDelivery(prior, current, ct))
                    return new(false, "incomplete_coverage");
                if (!(await ArtifactManifestFiles.ValidateAsync(current.RootDirectory, current.Manifest, ct)).Available)
                    return new(false, "delivery_changed");
                return await RetireFiles(prior, metadata, receipt, stage, automatic, sameRevision, ct);
            }
            finally { runGate.Release(); }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        { return new(false, "retained_or_pending", Message: error.Message); }
        finally { taskGate.Release(); }
    }

    private static AcquisitionSupersededDeliveryResult Completed(ArtifactRecord prior) =>
        new(true, "retired", prior.Manifest.Entries.Count, prior.Manifest.Entries.Sum(e => e.Length));

    private async Task<bool> CoversPreviousDelivery(ArtifactRecord previous, ArtifactRecord current, CancellationToken ct)
    {
        // Old directory corrections can have reliable operation ancestry but no per-path
        // disposition. Equal multisets prove that every byte-identical file survives, including
        // repeated identical files. Both formal directories are independently rehashed before use.
        if (SameContents(previous.Manifest, current.Manifest))
        {
            var parents = new Queue<string>(current.ParentArtifactIds);
            var visited = new HashSet<string>(StringComparer.Ordinal) {current.Id};
            while (parents.TryDequeue(out var id))
            {
                if (id == previous.Id) return true;
                if (!visited.Add(id)) continue;
                if (visited.Count > 10000 || await store.GetArtifactAsync(id, ct) is not { } parent) return false;
                foreach (var ancestor in parent.ParentArtifactIds) parents.Enqueue(ancestor);
            }
            return false;
        }
        return (await ArtifactFileCoverage.EvaluateAsync(store, previous, [current.Id], ct)).Complete;
    }

    internal static bool SameContents(ArtifactManifest left, ArtifactManifest right)
    {
        if (left.Entries.Count != right.Entries.Count) return false;
        var counts = left.Entries.GroupBy(e => (e.Length, e.Sha256)).ToDictionary(g => g.Key, g => g.Count());
        foreach (var entry in right.Entries)
        {
            var key = (entry.Length, entry.Sha256);
            if (!counts.TryGetValue(key, out var remaining) || remaining == 0) return false;
            counts[key] = remaining - 1;
        }
        return true;
    }

    private async Task<bool> IsReferenced(int taskId, ArtifactRecord source, string stage, bool excludeCurrentTask, CancellationToken ct)
    {
        if (await store.HasActiveConsumersAsync(source.OperationId, ct)) return true;
        var root = source.RootDirectory;
        var aliases = (workspaces ?? new FileProcessingWorkspaceResolver(app.AppDataDirectory, storage)).ReadRelocations();
        // An exact old-name redirect owns no additional files. Partial/ancestor mappings
        // cannot prove that this publication is their sole destination, so retain them.
        if (aliases.Any(a => Overlaps(a.SourceRoot, root) || Overlaps(a.SourceRoot, stage) ||
            Overlaps(a.DestinationRoot, stage) || Overlaps(a.DestinationRoot, root) && !SamePath(a.DestinationRoot, root))) return true;
        string Relocate(string path)
        {
            var alias = aliases.SingleOrDefault(a => Within(path, a.SourceRoot));
            return alias == null ? path : Path.GetFullPath(Path.Combine(alias.DestinationRoot, Path.GetRelativePath(alias.SourceRoot, path)));
        }
        bool ReferencesPath(string path, bool ancestors = false)
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var resolved = Relocate(path);
            return (ancestors ? Overlaps(resolved, root) || aliases.Any(a => Within(a.SourceRoot, path) && Overlaps(a.DestinationRoot, root))
                : Within(resolved, root)) || Within(resolved, stage);
        }
        bool Refers(string? json)
        {
            if (json == null) return false;
            using var document = JsonDocument.Parse(json);
            return Visit(document.RootElement, 0);
            bool Text(string text, int depth)
            {
                if (text.Equals(source.Id, StringComparison.OrdinalIgnoreCase) || ReferencesPath(text)) return true;
                // Workflow configJson can contain another JSON document as a string.
                var nested = text.TrimStart();
                if (nested.Length == 0 || nested[0] is not ('{' or '[')) return false;
                try
                {
                    using var inner = JsonDocument.Parse(text);
                    return Visit(inner.RootElement, depth + 1);
                }
                catch (JsonException)
                {
                    // Names and storage templates can start with brackets too. If malformed
                    // embedded JSON might still name this delivery, retain it conservatively.
                    return new[] {root, stage, source.Id}.Concat(aliases.Select(a => a.SourceRoot))
                        .Any(p => text.Contains(p, PathComparison) || text.Contains(JsonSerializer.Serialize(p)[1..^1], PathComparison));
                }
            }
            bool Visit(JsonElement value, int depth)
            {
                if (depth > 64) throw new InvalidDataException("A reference document exceeds its nesting limit.");
                return value.ValueKind switch
                {
                    JsonValueKind.String => Text(value.GetString()!, depth),
                    JsonValueKind.Object => value.EnumerateObject().Any(p => Text(p.Name, depth + 1) || Visit(p.Value, depth + 1)),
                    JsonValueKind.Array => value.EnumerateArray().Any(item => Visit(item, depth + 1)),
                    _ => false
                };
            }
        }
        // First project only root columns. Metadata can contain a large, nested processing ledger.
        // Only a manifest registered at an ancestor can also explicitly own a file under this root.
        var roots = await db.Set<ArtifactDbModel>().AsNoTracking().Where(a => a.Id != source.Id)
            .Select(a => new {a.Id, a.RootPath, a.RootIsAppData, a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData}).ToListAsync(ct);
        foreach (var registration in roots)
        {
            var paths = new List<string> { Relocate(Resolve(registration.RootPath, registration.RootIsAppData)) };
            if (registration.QuarantineSourceRootPath is { } alias)
                paths.Add(Relocate(Resolve(alias, registration.QuarantineSourceRootIsAppData)));
            foreach (var p in paths)
            {
                if (Within(p, root) || Within(p, stage)) return true;
                if (!Within(root, p) && !Within(stage, p) &&
                    !aliases.Any(a => Within(a.SourceRoot, p) && Overlaps(a.DestinationRoot, root))) continue;
                var other = await store.GetArtifactAsync(registration.Id, ct) ?? throw new InvalidOperationException("An artifact changed during reference checking.");
                if (ArtifactManifestFiles.ResolveFiles(p, other.Manifest).Concat(ArtifactManifestFiles.ResolveDirectories(p, other.Manifest))
                    .Any(f => ReferencesPath(f))) return true;
            }
        }
        var plans = await db.AcquisitionTasks.AsNoTracking().Where(t => (!excludeCurrentTask || t.Id != taskId) &&
            t.DeletedAt == null && t.PlanJson != null).Select(t => t.PlanJson).ToListAsync(ct);
        if (plans.Any(Refers)) return true;
        var active = await db.WorkflowRuns.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Pending ||
                r.Status == WorkflowRunStatus.Waiting || r.Status == WorkflowRunStatus.Running)
            .Select(r => new {r.PayloadJson, r.CurrentItemJson, r.PendingSignalJson, r.PlanSnapshotJson}).ToListAsync(ct);
        if (active.Any(r => Refers(r.PayloadJson) || Refers(r.CurrentItemJson) || Refers(r.PendingSignalJson) || Refers(r.PlanSnapshotJson))) return true;
        var resourcePaths = await db.ResourcesV2.AsNoTracking().Where(r => r.Path != null).Select(r => r.Path!).ToListAsync(ct);
        if (resourcePaths.Any(p => ReferencesPath(p, ancestors: true))) return true;
        var libraries = await db.MediaLibrariesV2.AsNoTracking().Select(l => l.Paths).ToListAsync(ct);
        if (libraries.SelectMany(MediaLibraryV2Extensions.ParsePaths).Any(p => ReferencesPath(p, ancestors: true))) return true;
        var fileLists = await db.DownloadResults.AsNoTracking().Select(d => d.FilesJson).ToListAsync(ct);
        foreach (var json in fileLists)
        {
            var files = JsonSerializer.Deserialize<string?[]>(json);
            if (files == null || files.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("A download result has an invalid file list.");
            if (files.Any(p => ReferencesPath(p!))) return true;
        }
        var downloadPaths = await db.DownloadTaskFiles.AsNoTracking().Select(f => f.Path)
            .Concat(db.DownloadResults.AsNoTracking().Select(d => d.Path))
            .Concat(db.DownloadTasks.AsNoTracking().Select(d => d.DownloadPath))
            .Concat(db.PathMarks.AsNoTracking().Where(m => !m.IsDeleted).Select(m => m.Path)).ToListAsync(ct);
        return downloadPaths.Any(p => ReferencesPath(p));

        string Resolve(string path, bool appData) => appData ? Path.Combine(app.AppDataDirectory, path) : path;
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), PathComparison);
    private static bool Within(string path, string root)
    {
        var full = Path.GetFullPath(root);
        return SamePath(path, full) || Path.GetFullPath(path).StartsWith(
            Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar, PathComparison);
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool Overlaps(string a, string b) => Within(a, b) || Within(b, a);

    // Intent commits before the same-volume move, which removes the published name in one operation.
    // Only a matching durable receipt can resume a staged purge after a crash. No recursive delete.
    private async Task<AcquisitionSupersededDeliveryResult> RetireFiles(ArtifactRecord source, JsonObject metadata,
        Receipt receipt, string stage, bool automatic, bool excludeCurrentTask, CancellationToken ct)
    {
        var root = source.RootDirectory;
        ArtifactManifestFiles.RejectLinks(root);
        ArtifactManifestFiles.RejectLinks(stage);
        bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        if (source.State == ArtifactLocationState.Available)
        {
            if (Exists(stage) || !Directory.Exists(root)) return new(false, "storage_conflict");
            await SupersededDeliveryFiles.CheckAsync(root, source.Manifest, partial: false, ct);
            if (automatic && options.Value.KeepSupersededDeliveryFiles) return new(false, "keep");
            await Save(ArtifactLocationState.Available, ArtifactLocationState.Quarantined, receipt);
        }
        if (Exists(root))
        {
            if (Exists(stage) || receipt.Purging) return new(false, "storage_conflict");
            await SupersededDeliveryFiles.CheckAsync(root, source.Manifest, partial: false, ct);
            // Resource/path registrations do not all use the artifact fence. Serialize the final
            // reference snapshot and the directory move against database writers. Hashing and
            // the subsequent purge remain outside this short transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await IsReferenced(receipt.TaskId, source, stage, excludeCurrentTask, ct)) return new(false, "referenced");
            Directory.Move(root, stage);
            await transaction.CommitAsync(ct);
        }
        if (!receipt.Purging)
        {
            await SupersededDeliveryFiles.CheckAsync(stage, source.Manifest, partial: false, ct);
            receipt = receipt with {Purging = true};
            await Save(ArtifactLocationState.Quarantined, ArtifactLocationState.Quarantined, receipt);
        }
        if (Directory.Exists(stage))
            await SupersededDeliveryFiles.PurgeAsync(stage, source.Manifest, ct);
        else if (File.Exists(stage) || !Directory.Exists(Directory.GetParent(stage)!.FullName))
            return new(false, "storage_unavailable");
        await Save(ArtifactLocationState.Quarantined, ArtifactLocationState.Retired, receipt);
        return Completed(source);

        async Task Save(ArtifactLocationState expected, ArtifactLocationState next, Receipt value)
        {
            metadata[ReceiptKey] = JsonSerializer.SerializeToNode(value, WorkflowJson.Options);
            var json = metadata.ToJsonString();
            var changed = await db.Set<ArtifactDbModel>().Where(a => a.Id == source.Id && a.OperationId == source.OperationId &&
                a.Kind == "publishedFiles" && !a.RootIsAppData && a.RootPath == root && a.Fingerprint == source.Fingerprint && a.State == expected)
                .ExecuteUpdateAsync(update => update.SetProperty(a => a.MetadataJson, json).SetProperty(a => a.State, next)
                    .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
            if (changed != 1) throw new InvalidOperationException("The registered delivery changed during retirement.");
        }
    }
}
