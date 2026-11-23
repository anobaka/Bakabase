using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionProcessingStageCommitReceipt(int Version, string Id, int TaskId, int RunId,
    string PartId, string BindingId, string Directory, string SourceArtifactId, string SourceFingerprint,
    string SourceDirectory, string Intent, AcquisitionStageDirectoryMove Move, IReadOnlyList<string> ReusedFiles,
    string? ArtifactId = null, string? OwnedArtifactId = null, string? Fingerprint = null,
    IReadOnlyList<string>? ReplacedInputsRemoved = null, IReadOnlyList<string>? BlockingInputsToRemove = null);

/// <summary>Hands complete, exclusively produced outputs back to the visible stage folder.
/// Exact manifests and durable move intent survive a restart; unknown siblings are not selected.</summary>
internal static partial class AcquisitionProcessingStageCommit
{
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;

    internal static async Task<ArtifactRecord> CommitAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target,
        ArtifactRecord output, CancellationToken ct)
    {
        if (target.StageDirectories?.Processing is not {BindingId: not null} binding ||
            Same(binding.Directory, output.RootDirectory) || !output.Managed || output.Kind != "processedFiles") return output;
        var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
        var ids = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == ctx.RunId &&
            s.Module == "acquisition" && s.EntityKind == "task").Select(s => s.EntityId).Distinct().ToArrayAsync(ct);
        if (ids is not [{ } id] || !int.TryParse(id, out var taskId)) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":process:stage-source:" + output.Id, output.OperationId, ct);
        var store = ctx.Services.GetRequiredService<IArtifactOperationStore>();
        var workspaces = ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var storage = ctx.Services.GetRequiredService<IUserStoragePolicy>();
        var committed = await CommitCoreAsync(db, store, storage, workspaces, taskId, (int)ctx.RunId,
            target.Id, binding, output, ct, new FileArtifactMaterializer(ctx.Services.GetRequiredService<IStorageWriteGuard>()));
        await ArtifactWorkflowBindings.BindAsync(ctx, target.Id + ":process:stage:" + committed.Id, committed.OperationId, ct);
        return committed;
    }

    internal static async Task<ArtifactRecord> CommitCoreAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId,
        AcquisitionStageDirectory binding, ArtifactRecord source, CancellationToken ct, IFileArtifactMaterializer? materializer = null)
    {
        if (!Guid.TryParseExact(binding.BindingId, "N", out _) || !source.Managed || source.Kind != "processedFiles")
            throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        var producer = await store.GetOperationAsync(source.OperationId, ct);
        if (producer is not {State: ArtifactOperationState.Completed, Kind: "files.process"} || producer.OutputArtifactId != source.Id)
            throw new InvalidOperationException("STAGE_COMMIT_SOURCE_UNAVAILABLE");
        var key = ArtifactManifestFiles.Hash("processing-stage-v1:" + taskId + ":" + partId + ":" + binding.BindingId + ":" + source.Id);
        var journalPath = JournalPath(workspaces, key);
        AcquisitionProcessingStageCommitReceipt journal;
        if (File.Exists(journalPath))
        {
            journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(await File.ReadAllTextAsync(journalPath, ct), Json)
                ?? throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
            if (journal.Id != key || journal.TaskId != taskId || journal.PartId != partId || journal.BindingId != binding.BindingId ||
                !Same(journal.Directory, binding.Directory) || journal.SourceArtifactId != source.Id || journal.SourceFingerprint != source.Fingerprint)
                throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
            if (journal.ArtifactId is {} existingId && await store.ResolveByIdAsync(existingId, ct) is {} existing)
            {await GuardAsync(db, store, storage, workspaces, taskId, runId, partId, binding, source, ct);
                await SetPendingAsync(db, taskId, key, clear: true, ct); return existing;}
        }
        else
        {
            await GuardAsync(db, store, storage, workspaces, taskId, runId, partId, binding, source, ct);
            if (binding.Allocation != null) await workspaces.EnsureScopedDirectoryAsync(binding.Allocation, ct);
            else if (!Directory.Exists(binding.Directory))
                throw new DirectoryNotFoundException("STAGE_COMMIT_DIRECTORY_UNAVAILABLE");
            var move = new List<AcquisitionStageDirectoryMoveFile>(); var reused = new List<string>();
            foreach (var entry in source.Manifest.Entries)
            {
                var original = Path.Combine(source.RootDirectory, entry.RelativePath);
                var destination = Path.Combine(binding.Directory, entry.RelativePath);
                ArtifactManifestFiles.RejectLinks(original); ArtifactManifestFiles.RejectLinks(destination);
                var file = new FileInfo(original);
                if (!file.Exists || file.Length != entry.Length) throw new IOException("STAGE_COMMIT_SOURCE_CHANGED");
                if (File.Exists(destination))
                {
                    if (!await MatchesAsync(destination, entry, ct)) throw new IOException("STAGE_COMMIT_TARGET_CONFLICT");
                    reused.Add(entry.RelativePath);
                }
                else move.Add(new(entry.RelativePath, entry.Length, file.LastWriteTimeUtc.Ticks));
            }
            var selection = new AcquisitionStageDirectoryMove(partId, "processing", source.RootDirectory, binding.Directory,
                true, move, source.Manifest.Directories ?? []);
            var blockers = FindDirectoryBlockers(binding, selection);
            AcquisitionStageDirectoryFileMove.CheckTargets(selection, blockers.Select(p => Path.Combine(binding.Directory, p))
                .ToHashSet(ArtifactManifestFiles.PathComparer));
            if (blockers.Count > 0 && await RetainBlockingInputAsync(db, store, workspaces, taskId, runId, partId, binding, source, blockers, ct))
                return source;
            journal = new(1, key, taskId, runId, partId, binding.BindingId!, binding.Directory, source.Id,
                source.Fingerprint, source.RootDirectory, producer.IntentKey, selection, reused, BlockingInputsToRemove: blockers);
            await SaveAsync(journalPath, journal, ct);
        }
        await SetPendingAsync(db, taskId, key, clear: false, ct);
        async Task Guard() => await GuardAsync(db, store, storage, workspaces, taskId, runId, partId, binding, source, ct);
        async Task Persist() => await SaveAsync(journalPath, journal, CancellationToken.None);
        // No store registration or consumer attachment is attempted under this exclusive
        // fence. The persistent task marker prevents a new continuation reading a partial set.
        await using (await store.AcquireMaintenanceAsync(ct))
        {
            await Guard();
            foreach (var relative in journal.ReusedFiles)
                if (!await MatchesAsync(Path.Combine(binding.Directory, relative), source.Manifest.Entries.Single(e => e.RelativePath == relative), ct))
                    throw new IOException("STAGE_COMMIT_TARGET_CHANGED");
            await RemoveDirectoryBlockersAsync(db, store, storage, workspaces, taskId, runId, partId, binding, source, journal, ct);
            await AcquisitionStageDirectoryFileMove.ExecuteAsync(journal.Move, journal.Id, materializer ?? new FileArtifactMaterializer(), Persist, Guard, ct);
        }
        // Successful processing already grants ownership of this exact output selection,
        // not of the whole user-selected folder or any unlisted siblings.
        var fingerprint = source.Fingerprint;
        if (journal.ReusedFiles.Count > 0 && journal.Move.Files.Count > 0 && journal.OwnedArtifactId == null)
        {
            // A selected existing file may be borrowed. Register only the newly moved
            // output leaves as owned; the full mixed view below retains that distinction.
            var owned = await store.AdoptAsync(new(ArtifactManifestFiles.Hash(key + ":owned"), "stage-owned:" + key,
                    "files.process", source.Fingerprint),
                new(binding.Directory, journal.Move.Files.Select(f => Path.Combine(binding.Directory, f.RelativePath)).ToArray(),
                    "processedFiles", true, [source.Id],
                    CreateMetadata: manifest => JsonSerializer.Serialize(new FileArtifactProcessingMetadata(source.Id,
                        JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "notRequired"}, Json), source.MetadataJson,
                        StoragePolicy: ReadMetadata(source)?.StoragePolicy,
                        FileDisposition: FileDispositionProofs.Create(source, manifest, new(source.Manifest.Entries
                            .Select(e => new FileLineageEntry(e.RelativePath, manifest.Entries.Any(f => f.RelativePath == e.RelativePath)
                                ? [e.RelativePath] : [])).ToArray(), source.Manifest, manifest))), Json)),
                "acquisition.stage-commit:" + taskId, ct);
            await BindAsync(db, runId, partId, owned, ct);
            journal = journal with {OwnedArtifactId = owned.Id}; await Persist();
        }
        var sourceMetadata = ReadMetadata(source);
        var metadata = new FileArtifactProcessingMetadata(source.Id, JsonSerializer.Serialize(new FileProcessingPlan {Requirement = "notRequired"}, Json),
            source.MetadataJson, StoragePolicy: sourceMetadata?.StoragePolicy,
            FileDisposition: FileDispositionProofs.Preserve(source, source.Manifest),
            DirectoryProvenance: FileArtifactDeliveryLayout.ReadProvenance(source),
            WorkspaceDirectoryScope: binding.Allocation)
        {
            RetainedArchiveFiles = sourceMetadata?.RetainedArchiveFiles, RetainedGroupTokens = sourceMetadata?.RetainedGroupTokens,
            StageCommit = journal with {Move = journal.Move with {Completed = true}}
        };
        var result = await store.AdoptAsync(new(key, producer.IntentKey, "files.process", source.Fingerprint),
            new(binding.Directory, ArtifactManifestFiles.ResolveFiles(binding.Directory, source.Manifest), "processedFiles", journal.ReusedFiles.Count == 0,
                journal.OwnedArtifactId is {} ownedId ? [source.Id, ownedId] : [source.Id],
                JsonSerializer.Serialize(metadata, Json), fingerprint, ArtifactManifestFiles.ResolveDirectories(binding.Directory, source.Manifest)),
            "acquisition.stage-commit:" + taskId, ct);
        await BindAsync(db, runId, partId, result, ct);
        journal = journal with {ArtifactId = result.Id, Fingerprint = result.Fingerprint}; await SaveAsync(journalPath, journal, CancellationToken.None);
        // A successful deterministic replacement, not an AI assertion, can retire a
        // previous working-stage leaf. Companion/key/unknown files are never inferred.
        var removed = await RemoveReplacedInputsAsync(db, store, storage, workspaces, taskId, runId, partId, binding, source, result, ct);
        journal = journal with {ReplacedInputsRemoved = removed.Concat(journal.BlockingInputsToRemove ?? []).Distinct(StringComparer.Ordinal).ToArray()}; await SaveAsync(journalPath, journal, CancellationToken.None);
        await SetPendingAsync(db, taskId, key, clear: true, CancellationToken.None);
        return result;
    }

    private static async Task<IReadOnlyList<string>> RemoveReplacedInputsAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId, AcquisitionStageDirectory binding,
        ArtifactRecord resultSource, ArtifactRecord committed, CancellationToken ct)
    {
        var processing = ReadMetadata(resultSource);
        if (processing?.StoragePolicy?.CleanupIntermediates != true || processing.InputArtifactId == null ||
            processing.FileDisposition == null) return [];
        var input = await store.GetArtifactAsync(processing.InputArtifactId, ct);
        if (input == null || !input.Managed || input.Kind != "processedFiles" ||
            processing.StoragePolicy.SourceCleanup == "keep" || ReadMetadata(input)?.StoragePolicy?.SourceCleanup == "keep" ||
            !Same(input.RootDirectory, binding.Directory) ||
            !FileDispositionProofs.ValidFor(processing.FileDisposition, input, resultSource)) return [];
        var entries = processing.FileDisposition.Entries.Where(e => e.Disposition == "replaced" &&
            !committed.Manifest.Entries.Any(f => FileProcessingFiles.PathComparer.Equals(f.RelativePath, e.Source.RelativePath)))
            .Select(e => e.Source).ToArray();
        if (entries.Length == 0) return [];
        await using var fence = await store.AcquireMaintenanceAsync(ct);
        if (!await ReplacedInputsUnreferencedAsync(db, store, workspaces, taskId, runId, partId, binding, input, entries, ct)) return [];
        var removed = new List<string>();
        foreach (var entry in entries)
        {
            var path = Path.Combine(binding.Directory, entry.RelativePath);
            storage.EnsureTreeMutationAllowed(path); ArtifactManifestFiles.RejectLinks(path);
            if (!File.Exists(path)) continue;
            var before = new FileInfo(path); var ticks = before.LastWriteTimeUtc.Ticks;
            if (!await MatchesAsync(path, entry, ct)) continue;
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != entry.Length || after.LastWriteTimeUtc.Ticks != ticks) continue;
            File.Delete(path); removed.Add(entry.RelativePath);
        }
        return removed;
    }

    private static async Task<bool> ReplacedInputsUnreferencedAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId, AcquisitionStageDirectory binding,
        ArtifactRecord input, IReadOnlyList<ArtifactManifestEntry> entries, CancellationToken ct)
    {
        var ownerIds = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && s.EntityId == taskId.ToString()).Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
        var executing = await db.AcquisitionTasks.AnyAsync(t => t.Id == taskId && t.WorkflowRunId == runId, ct)
            ? runId : (int?) null;
        if (await new ArtifactCleanupReferenceGuard(db, workspaces).HasReferencesAsync(input, ownerIds, ct,
            entries.Select(e => Path.Combine(input.RootDirectory, e.RelativePath)).ToArray(), previewSupersededRuns: ownerIds,
            directoryOwnerTaskId: taskId, directoryOwnerRunId: executing)) return false;
        var selected = entries.Select(e => Path.Combine(input.RootDirectory, e.RelativePath)).ToHashSet(ArtifactManifestFiles.PathComparer);
        var roots = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired)
            .Select(a => new {a.Id, a.RootPath, a.RootIsAppData, a.State}).ToListAsync(ct);
        foreach (var record in roots)
        {
            var root = await workspaces.ReadOriginalArtifactDirectoryAsync(new ArtifactDbModel
                {RootPath = record.RootPath, RootIsAppData = record.RootIsAppData, State = ArtifactLocationState.Available}, ct);
            if (root == null || !(Same(root, binding.Directory) || ArtifactCleanupPathReferences.StrictlyUnder(root, binding.Directory) ||
                ArtifactCleanupPathReferences.StrictlyUnder(binding.Directory, root))) continue;
            if (record.State == ArtifactLocationState.Quarantined) return false;
            var referenced = await store.GetArtifactAsync(record.Id, ct);
            if (referenced == null || !referenced.Files.Any(selected.Contains)) continue;
            if (referenced.State == ArtifactLocationState.Quarantined) return false;
            var owners = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == referenced.OperationId).ToListAsync(ct);
            if (owners.Count == 0 || owners.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out var owner) ||
                !ownerIds.Contains(owner) || !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal))) return false;
            if (await db.ArtifactConsumers.AnyAsync(c => c.OperationId == referenced.OperationId && c.ReleasedAt == null &&
                c.LeaseUntil > DateTime.UtcNow && (c.OwnerKind != "workflow" || c.OwnerId != runId.ToString()), ct)) return false;
        }
        return true;
    }

    private static async Task BindAsync(BakabaseDbContext db, int runId, string partId, ArtifactRecord artifact, CancellationToken ct)
    {
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(),
            partId + ":process:stage:" + artifact.Id, artifact.OperationId, ct)) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
    }

    internal static async Task<bool> IsOwnedViewAsync(BakabaseDbContext db, IArtifactOperationStore store,
        FileProcessingWorkspaceResolver? workspaces, ArtifactRecord view, int taskId, IReadOnlyList<int> ownerRunIds, CancellationToken ct)
    {
        if (view.Managed || view.Kind != "processedFiles" || workspaces == null || view.MetadataJson == null) return false;
        try
        {
            var metadata = ReadMetadata(view);
            if (metadata?.StageCommit is not {Version: 1} evidence || evidence.TaskId != taskId ||
                evidence.Id is not {Length: 64} || evidence.Id.Any(c => !Uri.IsHexDigit(c)) ||
                string.IsNullOrWhiteSpace(evidence.Directory) || string.IsNullOrWhiteSpace(evidence.SourceArtifactId) ||
                evidence.SourceFingerprint is not {Length: 64} || string.IsNullOrWhiteSpace(evidence.PartId) ||
                evidence.Move?.Files == null || evidence.ReusedFiles == null ||
                !ownerRunIds.Contains(evidence.RunId) || evidence.ReusedFiles.Count == 0 ||
                !Same(evidence.Directory, view.RootDirectory) || view.Fingerprint != view.Manifest.Fingerprint ||
                !Guid.TryParseExact(evidence.BindingId, "N", out _) ||
                !await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, evidence.RunId, taskId, ct)) return false;
            var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(
                await File.ReadAllTextAsync(JournalPath(workspaces, evidence.Id), ct), Json);
            if (journal?.Move?.Files == null || string.IsNullOrWhiteSpace(journal.Directory) ||
                journal.ArtifactId != view.Id || journal.Fingerprint != view.Fingerprint ||
                journal.SourceArtifactId != evidence.SourceArtifactId || journal.OwnedArtifactId != evidence.OwnedArtifactId ||
                journal.TaskId != taskId || journal.PartId != evidence.PartId || journal.BindingId != evidence.BindingId ||
                journal.RunId != evidence.RunId || !Same(journal.Directory, view.RootDirectory) ||
                !journal.Move.Files.All(f => f != null && f.Completed)) return false;
            var parents = evidence.OwnedArtifactId is {} owned ? new[] {evidence.SourceArtifactId, owned} : [evidence.SourceArtifactId];
            if (!view.ParentArtifactIds.ToHashSet(StringComparer.Ordinal).SetEquals(parents)) return false;
            var source = await store.GetArtifactAsync(evidence.SourceArtifactId, ct);
            var operation = await store.GetOperationAsync(view.OperationId, ct);
            if (source == null || source.Fingerprint != evidence.SourceFingerprint || metadata.InputArtifactId != source.Id ||
                operation is not {Kind: "files.process", State: ArtifactOperationState.Completed} || operation.OutputArtifactId != view.Id ||
                operation.InputFingerprint != source.Fingerprint || !FileDispositionProofs.ValidFor(metadata.FileDisposition, source, view)) return false;
            var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct);
            if (task == null) return false;
            var plan = task.PlanJson == null ? null : JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, Json);
            var history = task.PlanHistoryJson == null ? [] : JsonSerializer.Deserialize<AcquisitionPlanRevision[]>(task.PlanHistoryJson, Json) ?? [];
            bool MatchesBinding(AcquisitionPlan? p) => AcquisitionCurrentDirectoryFiles.Entry(p?.StageDirectories, evidence.PartId, "processing") is {} b &&
                b.BindingId == evidence.BindingId && Same(b.Directory, view.RootDirectory);
            if (!MatchesBinding(plan) && !history.Any(h => MatchesBinding(h.Plan))) return false;
            foreach (var artifactId in new[] {view.Id}.Concat(evidence.OwnedArtifactId == null ? [] : [evidence.OwnedArtifactId]))
            {
                var artifact = await store.GetArtifactAsync(artifactId, ct);
                if (artifact == null) return false;
                if (artifactId != view.Id)
                {
                    var originalRoot = artifact.RootDirectory;
                    if (!Same(originalRoot, view.RootDirectory))
                    {
                        var record = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == artifact.Id, ct);
                        originalRoot = record == null ? null : await workspaces.ReadQuarantinedArtifactOriginAsync(record, ct);
                    }
                    if (!artifact.Managed || originalRoot == null || !Same(originalRoot, view.RootDirectory) ||
                        artifact.Manifest.Entries.Any(e => !view.Manifest.Entries.Any(v => FileDispositionProofs.SameEntry(e, v)))) return false;
                }
                var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
                if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out var owner) ||
                    !ownerRunIds.Contains(owner) || !b.Slot.StartsWith(evidence.PartId + ":", StringComparison.Ordinal))) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {return false;}
    }

    internal static async Task RecoverAsync(BakabaseDbContext db, IArtifactOperationStore store, IUserStoragePolicy storage,
        FileProcessingWorkspaceResolver workspaces, int taskId, CancellationToken ct, IFileArtifactMaterializer? materializer = null)
    {
        var row = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var state = State(row.BuilderStateJson);
        if (state.ProcessingStageCommitId is not {} id) return;
        var path = JournalPath(workspaces, id);
        var journal = JsonSerializer.Deserialize<AcquisitionProcessingStageCommitReceipt>(await File.ReadAllTextAsync(path, ct), Json)
            ?? throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        if (journal.TaskId != taskId || journal.Id != id || await db.WorkflowRuns.AnyAsync(r => r.Id == journal.RunId &&
            (r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending || r.PendingSignalJson != null), ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, Json)!;
        var binding = AcquisitionCurrentDirectoryFiles.Entry(plan.StageDirectories, journal.PartId, "processing")
            ?? throw new InvalidDataException("STAGE_COMMIT_BINDING_CHANGED");
        var source = await store.GetArtifactAsync(journal.SourceArtifactId, ct) ?? throw new InvalidDataException("STAGE_COMMIT_SOURCE_UNAVAILABLE");
        var result = await CommitCoreAsync(db, store, storage, workspaces, taskId, journal.RunId, journal.PartId, binding, source, ct, materializer);
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", journal.RunId.ToString(),
            journal.PartId + ":process:stage:" + result.Id, result.OperationId, ct)) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
    }

    internal static async Task GuardAsync(BakabaseDbContext db, IArtifactOperationStore store, IUserStoragePolicy storage,
        FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId, AcquisitionStageDirectory binding, ArtifactRecord source, CancellationToken ct)
    {
        storage.EnsureTreeMutationAllowed(binding.Directory); storage.EnsureTreeMutationAllowed(source.RootDirectory);
        ArtifactManifestFiles.RejectLinks(binding.Directory); ArtifactManifestFiles.RejectLinks(source.RootDirectory);
        if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, taskId, ct)) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        var row = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId && t.DeletedAt == null, ct);
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, Json)!;
        var current = AcquisitionCurrentDirectoryFiles.Entry(plan.StageDirectories, partId, "processing");
        if (State(row.BuilderStateJson).DirectoryChange != null || current?.BindingId != binding.BindingId || !Same(current.Directory, binding.Directory))
            throw new InvalidOperationException("STAGE_COMMIT_BINDING_CHANGED");
        if (row.WorkflowRunId != runId && await db.WorkflowRuns.AnyAsync(r => r.Id == runId &&
                (r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending), ct))
            throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        var owners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == taskId.ToString())
            .Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
        if (await db.WorkflowRuns.AnyAsync(r => owners.Contains(r.Id) && r.Id != runId &&
            (r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending || r.PendingSignalJson != null), ct))
            throw new InvalidOperationException("STAGE_COMMIT_SHARED");
        var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == source.OperationId).ToListAsync(ct);
        if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out var owner) ||
                !owners.Contains(owner) || !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal)))
            throw new InvalidOperationException("STAGE_COMMIT_SHARED");
        var consumers = await db.ArtifactConsumers.AsNoTracking().Where(c => c.OperationId == source.OperationId && c.ReleasedAt == null).ToListAsync(ct);
        if (consumers.Any(c => c.LeaseUntil > DateTime.UtcNow && (c.OwnerKind != "workflow" || c.OwnerId != runId.ToString())))
            throw new InvalidOperationException("STAGE_COMMIT_SHARED");
        if (await store.HasOtherRegisteredFilesAsync(source.Files, source.Id, ct)) throw new InvalidOperationException("STAGE_COMMIT_SHARED");
        var references = new ArtifactCleanupReferenceGuard(db, workspaces);
        var executing = row.WorkflowRunId == runId ? runId : (int?) null;
        if (await references.HasReferencesAsync(source, owners, ct, previewSupersededRuns: owners,
                directoryOwnerTaskId: taskId, directoryOwnerRunId: executing))
            throw new InvalidOperationException("STAGE_COMMIT_REFERENCED");
        var destination = source with {Id = "stage-destination:" + taskId, RootDirectory = binding.Directory,
            Files = ArtifactManifestFiles.ResolveFiles(binding.Directory, source.Manifest)};
        if (await references.HasReferencesAsync(destination, owners, ct, previewSupersededRuns: owners,
                directoryOwnerTaskId: taskId, directoryOwnerRunId: executing, processingPartId: partId))
            throw new InvalidOperationException("STAGE_COMMIT_REFERENCED");
        var paths = destination.Files.ToHashSet(ArtifactManifestFiles.PathComparer);
        var roots = await db.Artifacts.AsNoTracking().Where(a => a.State != ArtifactLocationState.Retired && a.Id != source.Id)
            .Select(a => new {a.Id, a.RootPath, a.RootIsAppData, a.State, a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData}).ToListAsync(ct);
        foreach (var record in roots)
        {
            // The production ledger can contain GiB of immutable manifests. Resolve only
            // overlapping root candidates; never hydrate every manifest on each fresh guard.
            var root = await workspaces.ReadOriginalArtifactDirectoryAsync(new ArtifactDbModel
                {RootPath = record.RootPath, RootIsAppData = record.RootIsAppData, State = ArtifactLocationState.Available}, ct);
            var original = record.QuarantineSourceRootPath == null ? null : await workspaces.ReadOriginalArtifactDirectoryAsync(new ArtifactDbModel
                {RootPath = record.QuarantineSourceRootPath, RootIsAppData = record.QuarantineSourceRootIsAppData, State = ArtifactLocationState.Available}, ct);
            bool Overlaps(string? candidate) => candidate != null && (Same(candidate, binding.Directory) ||
                ArtifactCleanupPathReferences.StrictlyUnder(candidate, binding.Directory) || ArtifactCleanupPathReferences.StrictlyUnder(binding.Directory, candidate));
            if (!Overlaps(root) && !Overlaps(original)) continue;
            if (record.State == ArtifactLocationState.Quarantined) throw new InvalidOperationException("STAGE_COMMIT_SHARED");
            var artifact = await store.GetArtifactAsync(record.Id, ct);
            if (artifact == null || !artifact.Files.Any(paths.Contains)) continue;
            if (artifact.State == ArtifactLocationState.Quarantined) throw new InvalidOperationException("STAGE_COMMIT_SHARED");
            var referencesToOutput = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == artifact.OperationId).ToListAsync(ct);
            if (referencesToOutput.Count == 0 || referencesToOutput.Any(b => b.OwnerKind != "workflow" ||
                !int.TryParse(b.OwnerId, out var owner) || !owners.Contains(owner) || !b.Slot.StartsWith(partId + ":", StringComparison.Ordinal)))
                throw new InvalidOperationException("STAGE_COMMIT_SHARED");
        }
    }

    private static async Task SetPendingAsync(BakabaseDbContext db, int taskId, string id, bool clear, CancellationToken ct)
    {
        var json = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null).Select(t => t.BuilderStateJson).SingleAsync(ct);
        var state = State(json);
        if (state.ProcessingStageCommitId != null && state.ProcessingStageCommitId != id) throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
        if (clear && state.ProcessingStageCommitId == null) return;
        // The pending marker does not own the caller's complete builder document.
        // Preserve other modules' fields and older allocation records verbatim in shape.
        var document = System.Text.Json.Nodes.JsonNode.Parse(json ?? "{}") as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        if (clear) document.Remove("processingStageCommitId");
        else document["processingStageCommitId"] = id;
        var next = document.ToJsonString(Json);
        if (await db.AcquisitionTasks.Where(t => t.Id == taskId && t.DeletedAt == null && t.BuilderStateJson == json)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, next), ct) != 1)
            throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
    }
    private static AcquisitionPlanBuilderState State(string? json) => json == null ? new() : JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(json, Json)!;
    private static FileArtifactProcessingMetadata? ReadMetadata(ArtifactRecord source) => source.MetadataJson == null ? null : JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(source.MetadataJson, Json);
    private static bool Same(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));
    private static string JournalPath(FileProcessingWorkspaceResolver workspaces, string id)
    {
        if (id is not {Length: 64} || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        var path = Path.Combine(workspaces.StageCommitMetadataDirectory, id + ".json"); ArtifactManifestFiles.RejectLinks(path); return path;
    }
    private static async Task SaveAsync(string path, AcquisitionProcessingStageCommitReceipt journal, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ArtifactManifestFiles.RejectLinks(path);
        var temporary = path + ".pending"; ArtifactManifestFiles.RejectLinks(temporary);
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(journal, Json), ct); File.Move(temporary, path, true);
    }
    private static async Task<bool> MatchesAsync(string path, ArtifactManifestEntry entry, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path); var file = new FileInfo(path);
        if (!file.Exists || file.Length != entry.Length) return false;
        await using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
