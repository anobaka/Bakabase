using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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

internal sealed record AcquisitionStageInputReceipt(int Version, string Id, int TaskId, int RunId,
    string PartId, string BindingId, string Directory, string SourceArtifactId, string SourceFingerprint,
    string? Workspace = null, int WorkspaceAttempt = 0, IReadOnlyList<string>? StartedFiles = null,
    string? SnapshotArtifactId = null, string? ResultArtifactId = null, ArtifactStoragePolicy? StoragePolicy = null);

/// <summary>Establishes the first processing input without moving the download or making
/// a second payload copy. Its pending task marker prevents reading a partially populated stage.</summary>
internal static class AcquisitionProcessingStageInput
{
    private static readonly JsonSerializerOptions Json = WorkflowJson.Options;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    internal static async Task<ArtifactRecord> PrepareAsync(WorkflowExecutionContext ctx,
        CloudDownloadTarget target, ArtifactRecord input, CancellationToken ct, ArtifactStoragePolicy? policy = null)
    {
        if (target.StageDirectories?.Processing is not {BindingId: not null} binding ||
            Same(binding.Directory, input.RootDirectory)) return input;
        // Explicitly selected files keep their identity when another populated stage
        // already exists. An empty processing stage still needs its initial input before
        // assessment can suspend; otherwise the next processing retry sees an empty root.
        if (input.Kind == AcquisitionCurrentDirectoryFiles.Kind)
        {
            // A current-file selection may also be an already processed or delivered
            // directory. Only the bound download hand-off initializes a different root.
            if (target.StageDirectories.Download is not {} download || !Same(download.Directory, input.RootDirectory))
                return input;
            var workspaces = ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>();
            if (Directory.Exists(binding.Directory))
            {
                var excluded = (await workspaces.OwnedDirectoriesUnderAsync(binding.Directory, ct))
                    .Select(p => p.Directory).ToHashSet(ArtifactManifestFiles.PathComparer);
                if (AcquisitionStageDirectoryFileMove.Inspect(binding.Directory, excluded, ct).Files.Count > 0) return input;
            }
        }
        var db = ctx.Services.GetRequiredService<BakabaseDbContext>();
        var ids = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == ctx.RunId &&
            s.Module == "acquisition" && s.EntityKind == "task").Select(s => s.EntityId).Distinct().ToArrayAsync(ct);
        if (ids is not [{} id] || !int.TryParse(id, out var taskId))
            throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        return await PrepareCoreAsync(db, ctx.Services.GetRequiredService<IArtifactOperationStore>(),
            ctx.Services.GetRequiredService<IUserStoragePolicy>(), ctx.Services.GetRequiredService<FileProcessingWorkspaceResolver>(),
            taskId, (int)ctx.RunId, target.Id, binding, input,
            new FileArtifactMaterializer(ctx.Services.GetRequiredService<IStorageWriteGuard>()),
            ctx.Services.GetRequiredService<IServiceScopeFactory>(), ct, policy ?? target.StoragePolicy);
    }

    internal static async Task<ArtifactRecord> PrepareCoreAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId, string partId,
        AcquisitionStageDirectory binding, ArtifactRecord input, IFileArtifactMaterializer materializer,
        IServiceScopeFactory scopes, CancellationToken ct, ArtifactStoragePolicy? policy = null)
    {
        if (!Guid.TryParseExact(binding.BindingId, "N", out _)) throw new InvalidOperationException("PLAN_STAGE_DIRECTORY_INVALID");
        if (Same(binding.Directory, input.RootDirectory)) return input;
        if (input.Manifest.Entries.Count > 100000 || input.Manifest.Entries.Sum(e => (long)System.Text.Encoding.UTF8.GetByteCount(e.RelativePath)) > 4 * 1024 * 1024)
            throw new InvalidOperationException("STAGE_DIRECTORY_TOO_LARGE");
        var key = InputKey(taskId, runId, partId, binding, input);
        var path = ReceiptPath(workspaces, key);
        AcquisitionStageInputReceipt receipt;
        async Task Guard() => await GuardAsync(db, storage, taskId, runId, partId, binding, key, ct);
        await Guard();
        if (File.Exists(path))
        {
            receipt = await ReadAsync(path, ct);
            if (receipt.Id != key || receipt.TaskId != taskId || receipt.RunId != runId || receipt.PartId != partId ||
                receipt.BindingId != binding.BindingId || !Same(receipt.Directory, binding.Directory) ||
                receipt.SourceArtifactId != input.Id || receipt.SourceFingerprint != input.Fingerprint)
                throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
            if (receipt.ResultArtifactId is {} completed)
            {
                var reused = await store.ResolveByIdAsync(completed, ct)
                    ?? throw new InvalidDataException("STAGE_COMMIT_SOURCE_CHANGED");
                await SetPendingAsync(db, taskId, key, true, ct);
                return reused;
            }
        }
        else
        {
            if (binding.Allocation != null) await workspaces.EnsureScopedDirectoryAsync(binding.Allocation, ct);
            else if (!Directory.Exists(binding.Directory)) throw new DirectoryNotFoundException("STAGE_COMMIT_DIRECTORY_UNAVAILABLE");
            var excluded = (await workspaces.OwnedDirectoriesUnderAsync(binding.Directory, ct))
                .Select(p => p.Directory).ToHashSet(ArtifactManifestFiles.PathComparer);
            var current = AcquisitionStageDirectoryFileMove.Inspect(binding.Directory, excluded, ct);
            if (current.Files.Count > 0)
            {
                // An explicitly selected, populated stage is current input, not an invitation
                // to overlay an earlier download. Capture it without claiming deletion rights.
                var revision = await db.AcquisitionTasks.Where(t => t.Id == taskId).Select(t => t.PlanRevision).SingleAsync(ct);
                var captured = await AcquisitionCurrentDirectoryFiles.CaptureStageAsync(db, store, storage, workspaces,
                    taskId, revision, runId, partId, "processing", binding, Guard, ct);
                // Every boundary in this run must keep the same input identity. A new
                // user retry uses a new run/key and captures the then-current files.
                receipt = new(1, key, taskId, runId, partId, binding.BindingId!, binding.Directory,
                    input.Id, input.Fingerprint, ResultArtifactId: captured.Id, StoragePolicy: policy);
                await SaveAsync(path, receipt, ct);
                return captured;
            }
            receipt = new(1, key, taskId, runId, partId, binding.BindingId!, binding.Directory, input.Id, input.Fingerprint, StoragePolicy: policy);
            await SaveAsync(path, receipt, ct);
        }
        policy = receipt.StoragePolicy;
        await SetPendingAsync(db, taskId, key, false, ct);
        async Task Save() => await SaveAsync(path, receipt, CancellationToken.None);
        var consumer = "workflow:" + runId + ":stage-input:" + key;
        ArtifactRecord? snapshot = receipt.SnapshotArtifactId is {} snapshotId ? await store.GetArtifactAsync(snapshotId, ct) : null;
        if (snapshot == null)
        {
            var verified = await store.ResolveByIdAsync(input.Id, ct);
            if (verified == null || verified.Fingerprint != receipt.SourceFingerprint || !Same(verified.RootDirectory, input.RootDirectory))
                throw new InvalidDataException("STAGE_COMMIT_SOURCE_UNAVAILABLE");
            var sourceBindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == input.OperationId &&
                b.OwnerKind == "workflow" && b.OwnerId == runId.ToString() && b.Slot.StartsWith(partId + ":")).AnyAsync(ct);
            if (!sourceBindings || !await store.AttachConsumerAsync(input.OperationId, consumer, LeaseDuration, ct))
                throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
            var request = new ArtifactOperationRequest(key, "acquisition.stage-input:" + key, "files.process", input.Fingerprint);
            var claim = await store.ClaimAsync(request, consumer, LeaseDuration, ct);
            if (claim.Artifact != null) snapshot = claim.Artifact;
            else
            {
                if (claim.Lease is not {} lease) throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
                if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(), partId + ":process:stage-input:" + key, claim.OperationId, ct))
                    throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
                using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var stopHeartbeat = new CancellationTokenSource();
                // Recovery deliberately uses the stopped original run. Its private
                // reader therefore has a renewable lease, not a live-workflow pin.
                var heartbeat = RenewAsync(scopes, lease, input.OperationId, consumer, work, stopHeartbeat.Token);
                try
                {
                    if (receipt.Workspace == null)
                    {
                        var operation = await store.GetOperationAsync(claim.OperationId, work.Token)
                            ?? throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
                        var workspace = await workspaces.ResolveAsync("operations", key, operation.Attempt,
                            input.RootDirectory, binding.Directory, work.Token, newAttempt: true);
                        receipt = receipt with {Workspace = workspace, WorkspaceAttempt = operation.Attempt, StartedFiles = []}; await Save();
                    }
                    var owned = await workspaces.ReadExistingAsync("operations", key, receipt.WorkspaceAttempt, work.Token);
                    if (owned == null || !Same(owned, receipt.Workspace)) throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
                    var payload = Path.Combine(owned, "stage-input");
                    var expected = ArtifactManifestFiles.ResolveFiles(payload, input.Manifest).ToHashSet(ArtifactManifestFiles.PathComparer);
                    var started = (receipt.StartedFiles ?? []).ToHashSet(StringComparer.Ordinal);
                    if (started.Any(p => !input.Manifest.Entries.Any(e => e.RelativePath == p)))
                        throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
                    if (Directory.Exists(payload) && AcquisitionStageDirectoryFileMove.Inspect(payload,
                            new HashSet<string>(ArtifactManifestFiles.PathComparer), work.Token).Files
                        .Any(f => !expected.Contains(Path.Combine(payload, f.RelativePath))))
                        throw new InvalidDataException("STAGE_COMMIT_SOURCE_CHANGED");
                    Directory.CreateDirectory(payload); ArtifactManifestFiles.RejectLinks(payload);
                    if (started.Count == 0)
                    {
                        if (Directory.EnumerateFileSystemEntries(payload).Any()) throw new InvalidDataException("STAGE_COMMIT_TARGET_CONFLICT");
                        started = input.Manifest.Entries.Select(e => e.RelativePath).ToHashSet(StringComparer.Ordinal);
                        receipt = receipt with {StartedFiles = started.ToArray()}; await Save();
                    }
                    var entryIndex = 0;
                    foreach (var entry in input.Manifest.Entries)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        if (entryIndex++ % 64 == 0) await Guard();
                        var source = FileProcessingFiles.Within(Path.Combine(input.RootDirectory, entry.RelativePath), input.RootDirectory);
                        var target = FileProcessingFiles.Within(Path.Combine(payload, entry.RelativePath), payload);
                        if (File.Exists(target))
                        {
                            if (!started.Contains(entry.RelativePath)) throw new InvalidDataException("STAGE_COMMIT_TARGET_CONFLICT");
                            if (await MatchesAsync(target, entry, work.Token)) continue;
                            if (!await MatchesAsync(source, entry, work.Token) || !await IsPrefixAsync(target, source, entry.Length, work.Token))
                                throw new InvalidDataException("STAGE_COMMIT_SOURCE_CHANGED");
                            File.Delete(target); // Only a receipt-owned incomplete copy of the verified source.
                        }
                        if (Directory.Exists(target)) throw new InvalidDataException("STAGE_COMMIT_TARGET_CONFLICT");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        await materializer.MaterializeAsync(source, target, policy?.Materialization ?? "auto", work.Token);
                        if (!await MatchesAsync(target, entry, work.Token)) throw new InvalidDataException("STAGE_COMMIT_SOURCE_CHANGED");
                    }
                    ArtifactManifestFiles.CreateDirectories(payload, input.Manifest);
                    await Guard();
                    snapshot = await store.CompleteAsync(lease, new(payload, expected.ToArray(), "processedFiles", true, [input.Id],
                        JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id, "{\"requirement\":\"notRequired\",\"steps\":[]}",
                            input.MetadataJson, StoragePolicy: policy, FileDisposition: FileDispositionProofs.Preserve(input, input.Manifest),
                            DirectoryProvenance: FileArtifactDeliveryLayout.ReadProvenance(input)), Json),
                        input.Fingerprint, ArtifactManifestFiles.ResolveDirectories(payload, input.Manifest)), work.Token);
                }
                catch (Exception error)
                {
                    stopHeartbeat.Cancel(); await heartbeat;
                    await store.FailAsync(lease, error is OperationCanceledException ? "Stage input initialization cancelled." : "Stage input initialization interrupted.", ct: CancellationToken.None);
                    throw;
                }
                finally {stopHeartbeat.Cancel(); await heartbeat;}
            }
            receipt = receipt with {SnapshotArtifactId = snapshot.Id}; await Save();
        }
        await Guard();
        var result = await AcquisitionProcessingStageCommit.CommitCoreAsync(db, store, storage, workspaces,
            taskId, runId, partId, binding, snapshot, ct, materializer);
        receipt = receipt with {ResultArtifactId = result.Id}; await Save();
        await SetPendingAsync(db, taskId, key, true, CancellationToken.None);
        await store.ReleaseConsumerAsync(input.OperationId, consumer, CancellationToken.None);
        return result;
    }

    internal static async Task RecoverAsync(BakabaseDbContext db, IArtifactOperationStore store, IUserStoragePolicy storage,
        FileProcessingWorkspaceResolver workspaces, int taskId, IFileArtifactMaterializer materializer,
        IServiceScopeFactory scopes, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId && t.DeletedAt == null, ct);
        if (State(task.BuilderStateJson).ProcessingStageInputId is not {} id) return;
        var receipt = await ReadAsync(ReceiptPath(workspaces, id), ct);
        if (receipt.TaskId != taskId || receipt.Id != id || await db.WorkflowRuns.AnyAsync(r => r.Id == receipt.RunId &&
                (r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending || r.PendingSignalJson != null), ct))
            throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson!, Json)!;
        var binding = AcquisitionCurrentDirectoryFiles.Entry(plan.StageDirectories, receipt.PartId, "processing")
            ?? throw new InvalidOperationException("STAGE_COMMIT_BINDING_CHANGED");
        var input = await store.GetArtifactAsync(receipt.SourceArtifactId, ct)
            ?? throw new InvalidDataException("STAGE_COMMIT_SOURCE_UNAVAILABLE");
        using var writeScope = StorageWriteScope.Enter(new("workflow", receipt.RunId.ToString()));
        await PrepareCoreAsync(db, store, storage, workspaces, taskId, receipt.RunId, receipt.PartId,
            binding, input, materializer, scopes, ct, plan.StoragePolicy);
    }

    // The runner can re-enter its original processing cursor when its completed private input
    // is still bound to this run. Use the same ownership/reference checks as the eventual move.
    internal static async Task<bool> CanRetrySnapshotAsync(BakabaseDbContext db, IArtifactOperationStore store,
        IUserStoragePolicy storage, FileProcessingWorkspaceResolver workspaces, int taskId, int runId,
        CloudDownloadTarget target, string pending, CancellationToken ct)
    {
        if (target.StageDirectories?.Processing is not {BindingId: not null} binding ||
            target.DownloadArtifactId == null || target.ProcessedArtifactId != null ||
            target.PublishedArtifactId != null || target.ProcessingWaitReason != null || target.DeliveryReview != null)
            return false;
        var input = await store.GetArtifactAsync(target.DownloadArtifactId, ct);
        if (input is not {Kind: "download", State: ArtifactLocationState.Available} ||
            pending != InputKey(taskId, runId, target.Id, binding, input)) return false;
        var receipt = await ReadAsync(ReceiptPath(workspaces, pending), ct);
        if (receipt.Id != pending || receipt.TaskId != taskId || receipt.RunId != runId ||
            receipt.PartId != target.Id || receipt.BindingId != binding.BindingId || !Same(receipt.Directory, binding.Directory) ||
            receipt.SourceArtifactId != input.Id || receipt.SourceFingerprint != input.Fingerprint ||
            receipt.ResultArtifactId != null || receipt.SnapshotArtifactId == null) return false;
        var snapshot = await store.GetArtifactAsync(receipt.SnapshotArtifactId, ct);
        if (snapshot is not {Kind: "processedFiles", Managed: true, State: ArtifactLocationState.Available} ||
            snapshot.OperationId != pending || snapshot.Fingerprint != input.Fingerprint ||
            !snapshot.ParentArtifactIds.Contains(input.Id) || snapshot.Manifest.Entries.Count == 0) return false;
        var producer = await store.GetOperationAsync(pending, ct);
        if (producer is not {State: ArtifactOperationState.Completed, Kind: "files.process"} || producer.OutputArtifactId != snapshot.Id)
            return false;
        await GuardAsync(db, storage, taskId, runId, target.Id, binding, pending, ct);
        await AcquisitionProcessingStageCommit.GuardAsync(db, store, storage, workspaces, taskId, runId, target.Id, binding, snapshot, ct);
        foreach (var artifact in new[] {input, snapshot})
        foreach (var entry in artifact.Manifest.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = FileProcessingFiles.Within(Path.Combine(artifact.RootDirectory, entry.RelativePath), artifact.RootDirectory);
            storage.EnsurePathAllowed(path);
            ArtifactManifestFiles.RejectLinks(path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != entry.Length) return false;
        }
        return true;
    }

    private static async Task GuardAsync(BakabaseDbContext db, IUserStoragePolicy storage, int taskId, int runId,
        string partId, AcquisitionStageDirectory binding, string key, CancellationToken ct)
    {
        storage.EnsureTreeMutationAllowed(binding.Directory); ArtifactManifestFiles.RejectLinks(binding.Directory);
        if (!await AcquisitionPlanArtifactInputGuard.OwnsRunAsync(db, runId, taskId, ct)) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        var row = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == taskId && t.DeletedAt == null, ct);
        if (row.WorkflowRunId != runId) throw new InvalidOperationException("STAGE_COMMIT_OWNER_INVALID");
        var state = State(row.BuilderStateJson);
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(row.PlanJson!, Json)!;
        var current = AcquisitionCurrentDirectoryFiles.Entry(plan.StageDirectories, partId, "processing");
        if (state.DirectoryChange != null || current?.BindingId != binding.BindingId || !Same(current.Directory, binding.Directory))
            throw new InvalidOperationException("STAGE_COMMIT_BINDING_CHANGED");
        if (state.ProcessingStageInputId != null && state.ProcessingStageInputId != key)
            throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
    }
    private static async Task SetPendingAsync(BakabaseDbContext db, int taskId, string id, bool clear, CancellationToken ct)
    {
        var json = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null).Select(t => t.BuilderStateJson).SingleAsync(ct);
        var state = State(json);
        if (state.ProcessingStageInputId != null && state.ProcessingStageInputId != id) throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
        if (clear && state.ProcessingStageInputId == null) return;
        var document = JsonNode.Parse(json ?? "{}") as JsonObject ?? throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        if (clear) document.Remove("processingStageInputId");
        else document["processingStageInputId"] = id;
        var next = document.ToJsonString(Json);
        if (await db.AcquisitionTasks.Where(t => t.Id == taskId && t.DeletedAt == null && t.BuilderStateJson == json)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, next), ct) != 1)
            throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
    }
    private static AcquisitionPlanBuilderState State(string? json) => json == null ? new() : JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(json, Json)!;
    private static string ReceiptPath(FileProcessingWorkspaceResolver workspaces, string id)
    {
        if (id is not {Length: 64} || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        var path = Path.Combine(workspaces.StageCommitMetadataDirectory, "inputs", id + ".json"); ArtifactManifestFiles.RejectLinks(path); return path;
    }
    private static async Task<AcquisitionStageInputReceipt> ReadAsync(string path, CancellationToken ct)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
        var value = JsonSerializer.Deserialize<AcquisitionStageInputReceipt>(await File.ReadAllTextAsync(path, ct), Json);
        return value is {Version: 1} ? value : throw new InvalidDataException("STAGE_COMMIT_JOURNAL_INVALID");
    }
    private static async Task SaveAsync(string path, AcquisitionStageInputReceipt receipt, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); ArtifactManifestFiles.RejectLinks(path);
        var temporary = path + ".pending"; ArtifactManifestFiles.RejectLinks(temporary);
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt, Json), ct); File.Move(temporary, path, true);
    }
    private static string InputKey(int taskId, int runId, string partId, AcquisitionStageDirectory binding, ArtifactRecord input) =>
        ArtifactManifestFiles.Hash("stage-input-v1:" + taskId + ":" + runId + ":" + partId + ":" + binding.BindingId + ":" + input.Id + ":" + input.Fingerprint);
    private static bool Same(string a, string b) => AcquisitionStageDirectoryFileMove.Same(a, b);
    private static async Task<bool> MatchesAsync(string path, ArtifactManifestEntry expected, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path); var file = new FileInfo(path);
        if (!file.Exists || file.Length != expected.Length) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static async Task<bool> IsPrefixAsync(string candidate, string original, long fullLength, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(candidate); ArtifactManifestFiles.RejectLinks(original);
        await using var partial = File.OpenRead(candidate); await using var source = File.OpenRead(original);
        if (partial.Length >= fullLength || source.Length != fullLength) return false;
        var a = new byte[65536]; var b = new byte[65536];
        for (long remaining = partial.Length; remaining > 0;)
        {
            var count = (int)Math.Min(a.Length, remaining); await partial.ReadExactlyAsync(a.AsMemory(0, count), ct);
            await source.ReadExactlyAsync(b.AsMemory(0, count), ct);
            if (!a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count))) return false;
            remaining -= count;
        }
        return true;
    }
    private static async Task RenewAsync(IServiceScopeFactory scopes, ArtifactOperationLease lease,
        string sourceOperationId, string sourceConsumer, CancellationTokenSource work, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = scopes.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IArtifactOperationStore>();
                if (!await store.RenewExistingConsumerAsync(sourceOperationId, sourceConsumer, LeaseDuration, ct))
                    throw new InvalidOperationException("STAGE_COMMIT_SOURCE_UNAVAILABLE");
                if (await store.RenewAsync(lease, LeaseDuration, ct)) continue;
                if ((await store.GetOperationAsync(lease.OperationId, ct))?.State == ArtifactOperationState.Completed) return;
                throw new InvalidOperationException("STAGE_DIRECTORY_COMMIT_PENDING");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch {work.Cancel(); throw;}
    }
}
