using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionTaskDeleteOptions(bool CleanupFiles, bool AcceptDeliveredFiles,
    bool SkipFileVerification, bool RemoveTaskIfCleanupFails, bool RetryRetainedFiles = false, bool DeleteDeliveredFiles = false)
{
    internal bool HasFileCleanup => CleanupFiles || DeleteDeliveredFiles;
    internal static AcquisitionTaskDeleteOptions ForSubmit(AcquisitionTaskDeletePreview preview, AcquisitionTaskDeleteInput input)
    {
        var options = new AcquisitionTaskDeleteOptions(input.CleanupFiles ?? preview.CleanupFiles,
            input.AcceptDeliveredFiles ?? preview.AcceptDeliveredFiles, input.SkipFileVerification ?? preview.SkipFileVerification,
            input.RemoveTaskIfCleanupFails == true, input.RetryRetainedFiles == true,
            input.DeleteDeliveredFiles ?? preview.DeleteDeliveredFiles);
        if (options.RetryRetainedFiles != preview.RetryRetainedFiles)
            throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
        // A detailed inventory freezes the exact file selection and proof mode. An ordinary task
        // preview freezes the task identities and lets the user choose cleanup at confirmation.
        if ((preview.CleanupFiles || preview.DeleteDeliveredFiles) && (options.CleanupFiles != preview.CleanupFiles ||
                options.AcceptDeliveredFiles != preview.AcceptDeliveredFiles || options.SkipFileVerification != preview.SkipFileVerification) ||
            options.DeleteDeliveredFiles != preview.DeleteDeliveredFiles)
            throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
        options.Validate(preview.TotalCount);
        return options;
    }

    internal void Validate(int count)
    {
        if (RetryRetainedFiles && (!HasFileCleanup || RemoveTaskIfCleanupFails))
            throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
        if (HasFileCleanup && count > 100) throw new ArgumentException("CLEANUP_SELECTION_LIMIT");
        if (AcceptDeliveredFiles && !HasFileCleanup) throw new ArgumentException("CLEANUP_ACCEPTANCE_REQUIRES_CLEANUP");
        if (SkipFileVerification && !AcceptDeliveredFiles) throw new ArgumentException("CLEANUP_SKIP_VERIFICATION_REQUIRES_ACCEPTANCE");
        if (RemoveTaskIfCleanupFails && !HasFileCleanup) throw new ArgumentException("CLEANUP_FALLBACK_REQUIRES_CLEANUP");
    }
}

/// <summary>Short-lived server-owned confirmation, fixed identities and versions. Restart requires a new preview.</summary>
public sealed class AcquisitionTaskDeletePreviewStore
{
    internal sealed record Entry(AcquisitionTaskDeletePreview Preview, IReadOnlyDictionary<int, string> Versions,
        IReadOnlyDictionary<int, IReadOnlyList<int>> Runs,
        IReadOnlyDictionary<int, AcquisitionTaskDeletionCleanupService.Plan>? CleanupPlans = null)
    {
        public bool Claimed { get; set; }
        public AcquisitionTaskDeleteOptions? Options { get; set; }
        public AcquisitionTaskDeleteResult? Result { get; set; }
        public AcquisitionTaskDeleteProgress Progress { get; set; } = new(Preview.PreviewId, "preview",
            Preview.TotalCount, 0, null, "rechecking", 0, 0, null, DateTimeOffset.UtcNow);
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var (key, value) in _entries)
            lock (value)
                if (Expired(value)) _entries.TryRemove(key, out _);
        if (_entries.Count >= 100) throw new ArgumentException("ACQUISITION_DELETE_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewId] = entry;
    }
    private static bool Expired(Entry entry) => entry.Progress.State != "running" &&
        (entry.Claimed ? entry.Progress.UpdatedAt.AddMinutes(10) : entry.Preview.ExpiresAt) < DateTimeOffset.UtcNow;

    internal AcquisitionTaskDeleteProgress ReadProgress(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !_entries.TryGetValue(id, out var entry))
            throw new ArgumentException("ACQUISITION_DELETE_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (Expired(entry)) throw new ArgumentException("ACQUISITION_DELETE_PREVIEW_EXPIRED");
            return entry.Progress;
        }
    }
    internal Entry Claim(string id) => Claim(new AcquisitionTaskDeleteInput(id));
    internal Entry Claim(AcquisitionTaskDeleteInput input)
    {
        if (!Guid.TryParseExact(input.PreviewId, "N", out _) || !_entries.TryGetValue(input.PreviewId, out var entry))
            throw new ArgumentException("ACQUISITION_DELETE_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (Expired(entry)) throw new ArgumentException("ACQUISITION_DELETE_PREVIEW_EXPIRED");
            var options = AcquisitionTaskDeleteOptions.ForSubmit(entry.Preview, input);
            if (entry.Options != null && options != entry.Options) throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
            if (entry.Claimed && entry.Result == null) throw new ArgumentException("ACQUISITION_DELETE_OUTCOME_UNKNOWN");
            if (!entry.Claimed)
            {
                entry.Options = options;
                entry.Progress = entry.Progress with { State = "running", StartedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow, FinishedTasks = entry.Preview.Rows.Count(r =>
                        !r.CanDelete && !(options.RemoveTaskIfCleanupFails && r.CanRemoveTaskIfCleanupFails)) };
            }
            entry.Claimed = true; return entry;
        }
    }
}

public sealed class AcquisitionTaskDeletionService(BakabaseDbContext db, IAcquisitionDeletionActivityReader activity,
    AcquisitionTaskDeletePreviewStore previews, AcquisitionTaskDeletionCleanupService? cleanup = null,
    ILogger<AcquisitionTaskDeletionService>? logger = null)
{
    public AcquisitionTaskDeleteProgress GetProgress(string previewId) => previews.ReadProgress(previewId);
    public async Task<AcquisitionTaskDeletePreview> PreviewAsync(AcquisitionTaskDeletePreviewInput input, CancellationToken ct = default,
        Func<string, Task>? guardDirectory = null)
    {
        if (input.TaskIds == null || input.TaskIds.Count is 0 or > 10000 || input.TaskIds.Any(id => id <= 0))
            throw new ArgumentException("Choose between one and 10000 fixed acquisition task IDs.");
        var ids = input.TaskIds.Distinct().ToArray();
        var options = new AcquisitionTaskDeleteOptions(input.CleanupFiles == true, input.AcceptDeliveredFiles == true,
            input.SkipFileVerification == true, false, input.RetryRetainedFiles == true, input.DeleteDeliveredFiles == true);
        options.Validate(ids.Length);
        if (options.HasFileCleanup && cleanup == null) throw new ArgumentException("CLEANUP_UNAVAILABLE");
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var states = await activity.ReadAsync(tasks.Where(t => (t.DeletedAt != null) == options.RetryRetainedFiles).ToArray(), ct);
        var rows = ids.Select(id =>
        {
            var task = tasks.SingleOrDefault(t => t.Id == id);
            var reason = BlockReason(task, states.GetValueOrDefault(id), options.RetryRetainedFiles);
            return new AcquisitionTaskDeleteRow(id, task?.Title, reason == null, reason, CanRemoveTaskIfCleanupFails: !options.RetryRetainedFiles && reason == null);
        }).ToArray();
        var cleanupPlans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>();
        if (options.HasFileCleanup)
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (!row.CanDelete) continue;
                try
                {
                    var plan = await cleanup!.PreviewAsync(tasks.Single(t => t.Id == row.TaskId), states[row.TaskId].RunIds,
                        input.AcceptDeliveredFiles == true, input.SkipFileVerification == true, ct, guardDirectory, options.RetryRetainedFiles,
                        cleanupIntermediates: options.CleanupFiles, deleteDeliveredFiles: options.DeleteDeliveredFiles);
                    cleanupPlans[row.TaskId] = plan;
                    rows[i] = row with {Cleanup = plan.Summary, CanDelete = plan.Summary.ReasonCode == null,
                        ReasonCode = plan.Summary.ReasonCode, CanRemoveTaskIfCleanupFails = !options.RetryRetainedFiles && AllowsRecordRemoval(plan.Summary.ReasonCode)};
                }
                catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                { rows[i] = row with {CanDelete = false, CanRemoveTaskIfCleanupFails = false, ReasonCode = "CLEANUP_PATH_NOT_AUTHORIZED"}; }
                catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
                {
                    logger?.LogWarning(error, "Unable to preview cleanup for acquisition task {TaskId}", row.TaskId);
                    rows[i] = row with {CanDelete = false, ReasonCode = "CLEANUP_PREVIEW_FAILED"};
                }
            }
        ProtectOverlappingSelections(cleanupPlans);
        rows = rows.Select(row =>
        {
            if (!cleanupPlans.TryGetValue(row.TaskId, out var plan)) return row;
            var retainedReason = options.RetryRetainedFiles && row.CanDelete ? RetainedPreviewReason(plan.Summary) : null;
            return row with {Cleanup = plan.Summary, CanDelete = row.CanDelete && retainedReason == null,
                ReasonCode = retainedReason ?? row.ReasonCode};
        }).ToArray();
        var uniqueFiles = AcquisitionTaskDeletionCleanupService.Unique(rows.Where(r => r.CanDelete)
            .SelectMany(r => r.Cleanup?.Groups.Where(g => g.Eligible).SelectMany(g => g.Files) ?? []));
        var identifiedFiles = AcquisitionTaskDeletionCleanupService.Unique(rows.SelectMany(r => r.Cleanup?.Groups.SelectMany(g => g.Files) ?? []));
        rows = rows.Select(row => row.Cleanup == null ? row : row with {Cleanup = row.Cleanup with
            {Groups = row.Cleanup.Groups.Select(g => g with {Files = g.Files.Take(200).ToArray(), FilesTruncated = g.Files.Count > 200}).ToArray()}}).ToArray();
        var ready = rows.Where(r => r.CanDelete).Select(r => r.TaskId).ToHashSet();
        var preview = new AcquisitionTaskDeletePreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            ids.Length, ready.Count, ids.Length - ready.Count, rows, input.CleanupFiles == true,
            input.AcceptDeliveredFiles == true, uniqueFiles.Count, uniqueFiles.Sum(f => f.Bytes), identifiedFiles.Count,
            identifiedFiles.Sum(f => f.Bytes), options.HasFileCleanup && rows.Any(r => r.Cleanup?.SizeIncomplete != false),
            input.SkipFileVerification == true, options.RetryRetainedFiles, options.DeleteDeliveredFiles);
        var confirmed = rows.Where(r => r.CanDelete || r.CanRemoveTaskIfCleanupFails).Select(r => r.TaskId).ToHashSet();
        previews.Add(new(preview, tasks.Where(t => confirmed.Contains(t.Id)).ToDictionary(t => t.Id, t => Version(t, states[t.Id])),
            states.Where(s => confirmed.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value.RunIds), cleanupPlans));
        return preview;
    }

    public async Task<AcquisitionTaskDeleteResult> DeleteAsync(AcquisitionTaskDeleteInput input, CancellationToken ct = default,
        Func<string, Task>? guardDirectory = null)
    {
        var entry = previews.Claim(input);
        if (entry.Result is {} saved)
        {
            // Another session may hold the same idempotency key. Re-authorize paths in the response.
            foreach (var path in (saved.CleanupResults ?? []).SelectMany(r => r.RetainedDirectories ?? []).Distinct())
                await cleanup!.EnsureDirectoryAuthorizedAsync(path, guardDirectory);
            return saved;
        }
        var options = entry.Options!;
        var selected = entry.Preview.Rows.Where(r => r.CanDelete || options.RemoveTaskIfCleanupFails && r.CanRemoveTaskIfCleanupFails)
            .ToDictionary(r => r.TaskId);
        var skipped = entry.Preview.Rows.Where(r => !selected.ContainsKey(r.TaskId))
            .Select(r => new AcquisitionTaskDeleteSkip(r.TaskId, r.ReasonCode!)).ToList();
        var taskLocks = new List<(int Id, SemaphoreSlim Gate)>(); var runLocks = new List<SemaphoreSlim>();
        var blockedRuns = new HashSet<int>(); var deleted = new List<int>();
        var cleanupResults = new List<AcquisitionTaskDeleteCleanupOutcome>();
        try
        {
            // A confirmation can arrive through a different remote session. Authorize the
            // whole fixed selection before cancelling an obsolete run or unlinking any file.
            if ((entry.Preview.CleanupFiles || entry.Preview.DeleteDeliveredFiles) && cleanup != null)
                foreach (var plan in (entry.CleanupPlans ?? new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>()).Values)
                    await cleanup.EnsureAuthorizedAsync(plan, guardDirectory, ct);
            // Do not wait behind a network operation: a task currently being edited/executed is skipped.
            foreach (var id in selected.Keys.Order())
            {
                var gate = AcquisitionTaskGate.For(id);
                if (await gate.WaitAsync(0, ct)) taskLocks.Add((id, gate));
                else
                {
                    skipped.Add(new(id, "ACQUISITION_TASK_ACTIVE"));
                    Report(entry, p => p with { FinishedTasks = p.FinishedTasks + 1 });
                }
            }
            foreach (var runId in taskLocks.SelectMany(t => entry.Runs[t.Id]).Distinct().Order())
            {
                var gate = WorkflowRunMutationGate.For(runId);
                if (await gate.WaitAsync(0, ct)) runLocks.Add(gate); else blockedRuns.Add(runId);
            }
            var ids = taskLocks.Select(t => t.Id).ToArray();
            var current = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            var states = await activity.ReadAsync(current.Values.Where(t => (t.DeletedAt != null) == options.RetryRetainedFiles).ToArray(), ct);
            string? CheckTask(int id)
            {
                var task = current.GetValueOrDefault(id);
                var state = states.GetValueOrDefault(id);
                if (BlockReason(task, state, options.RetryRetainedFiles) is {} reason) return reason;
                if (entry.Runs[id].Any(blockedRuns.Contains)) return "ACQUISITION_TASK_ACTIVE";
                return Version(task!, state!) != entry.Versions[id] ? "ACQUISITION_TASK_CHANGED" : null;
            }
            var deferredPlans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>();
            if (options.HasFileCleanup && !entry.Preview.CleanupFiles && !entry.Preview.DeleteDeliveredFiles)
            {
                // The user confirmed the task IDs and cleanup policy without opening an inventory.
                // Resolve the whole fixed selection in their current access scope before any mutation.
                if (cleanup == null) throw new ArgumentException("CLEANUP_UNAVAILABLE");
                foreach (var id in ids.Where(id => CheckTask(id) == null))
                {
                    Report(entry, p => p with { CurrentTaskId = id, Phase = "rechecking" });
                    var preparing = Stopwatch.GetTimestamp();
                    try
                    {
                        var plan = await cleanup.PreviewAsync(current[id], entry.Runs[id], options.AcceptDeliveredFiles,
                            options.SkipFileVerification, ct, guardDirectory);
                        deferredPlans.Add(id, plan);
                    }
                    catch (Exception error) when (error is not UserStoragePathException &&
                        error is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
                    { /* No file plan or paths are exposed. Record removal still requires current task safety. */ }
                    finally
                    {
                        logger?.LogInformation("Acquisition cleanup preparation for task {TaskId} took {ElapsedMs} ms. Skip content checks: {SkipContentChecks}; plan ready: {PlanReady}",
                            id, Stopwatch.GetElapsedTime(preparing).TotalMilliseconds, options.SkipFileVerification, deferredPlans.ContainsKey(id));
                    }
                }
                ProtectOverlappingSelections(deferredPlans);
            }
            foreach (var id in ids)
            {
                Report(entry, p => p with { CurrentTaskId = id, Phase = "rechecking" });
                var taskFinished = false;
                var executing = Stopwatch.GetTimestamp();
                try
                {
                    var task = current.GetValueOrDefault(id);
                    var reason = CheckTask(id);
                    if (reason != null) {skipped.Add(new(id, reason)); taskFinished = true; continue;}
                    if (options.HasFileCleanup)
                    {
                        var plan = entry.Preview.CleanupFiles || entry.Preview.DeleteDeliveredFiles ? entry.CleanupPlans?.GetValueOrDefault(id) : deferredPlans.GetValueOrDefault(id);
                        Report(entry, p => p with { Phase = "cleaning" });
                        var priorFiles = cleanupResults.Sum(r => r.RemovedFiles);
                        var priorBytes = cleanupResults.Sum(r => r.RemovedBytes);
                        var outcome = cleanup != null && plan != null
                            ? await cleanup.ApplyAsync(task!, plan, ct, (files, bytes) => Report(entry,
                                p => p with { CompletedFiles = priorFiles + files, CompletedBytes = priorBytes + bytes }))
                            : new AcquisitionTaskDeleteCleanupOutcome(id, false, 0, 0, [selected[id].ReasonCode ?? "CLEANUP_PREVIEW_FAILED"]);
                        cleanupResults.Add(outcome);
                        Report(entry, p => p with { CompletedFiles = priorFiles + outcome.RemovedFiles,
                            CompletedBytes = priorBytes + outcome.RemovedBytes });
                        if (!outcome.Completed)
                        {
                            if (!options.RemoveTaskIfCleanupFails || !outcome.ReasonCodes.All(AllowsRecordRemoval))
                            { skipped.Add(new(id, "CLEANUP_FILES_RETAINED")); taskFinished = true; continue; }
                            // A file failure is independent from record removal. Recheck activity before
                            // accepting a partial result, including unexpected changes during a long scan.
                            var latestTask = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
                            var latest = await activity.ReadAsync(latestTask is { DeletedAt: null } ? [latestTask] : [], ct);
                            var now = latest.GetValueOrDefault(id);
                            var block = BlockReason(latestTask, now);
                            if (block == null && (Version(latestTask!, states[id]) != entry.Versions[id] ||
                                !states[id].MatchesAfterCleanup(now!, outcome.ClosedRunIds)))
                                block = "ACQUISITION_TASK_CHANGED";
                            if (block != null) { skipped.Add(new(id, block)); taskFinished = true; continue; }
                        }
                    }
                    if (options.RetryRetainedFiles) { taskFinished = true; continue; }
                    // Soft deletion preserves evidence for both completed and retained cleanup outcomes.
                    Report(entry, p => p with { Phase = "deletingRecord" });
                    var changed = await db.AcquisitionTasks.Where(t => t.Id == id && t.DeletedAt == null &&
                        t.UpdatedAt == task!.UpdatedAt && t.PlanRevision == task.PlanRevision && t.WorkflowRunId == task.WorkflowRunId && t.Status == task.Status)
                        .ExecuteUpdateAsync(update => update.SetProperty(t => t.DeletedAt, (DateTime?)DateTime.UtcNow), ct);
                    if (changed == 1)
                    {
                        deleted.Add(id);
                        var outcome = cleanupResults.LastOrDefault(r => r.TaskId == id);
                        logger?.LogInformation("Removed acquisition task {TaskId}. Cleanup requested: {Requested}; removed files: {Files}; retained reasons: {Reasons}",
                            id, options.CleanupFiles, outcome?.RemovedFiles ?? 0, string.Join(",", outcome?.ReasonCodes ?? []));
                    }
                    else skipped.Add(new(id, "ACQUISITION_TASK_CHANGED"));
                    taskFinished = true;
                }
                finally
                {
                    logger?.LogInformation("Acquisition deletion execution for task {TaskId} took {ElapsedMs} ms. Finished: {Finished}",
                        id, Stopwatch.GetElapsedTime(executing).TotalMilliseconds, taskFinished);
                    if (taskFinished) Report(entry, p => p with { FinishedTasks = p.FinishedTasks + 1, CurrentTaskId = null });
                }
            }
            var result = new AcquisitionTaskDeleteResult(deleted, skipped, cleanupResults,
                cleanupResults.Sum(r => r.RemovedFiles), cleanupResults.Sum(r => r.RemovedBytes));
            lock (entry)
            {
                entry.Result = result;
                entry.Progress = entry.Progress with { State = "completed", Phase = "completed", CurrentTaskId = null,
                    FinishedTasks = entry.Preview.TotalCount, UpdatedAt = DateTimeOffset.UtcNow };
            }
            return result;
        }
        catch
        {
            Report(entry, p => p with { State = "interrupted", Phase = "interrupted" });
            throw;
        }
        // An interrupted/uncertain response cannot replay the claimed preview; a fresh explicit review is required.
        finally
        {
            foreach (var gate in runLocks) gate.Release();
            foreach (var (_, gate) in taskLocks) gate.Release();
        }
    }
    private static void Report(AcquisitionTaskDeletePreviewStore.Entry entry,
        Func<AcquisitionTaskDeleteProgress, AcquisitionTaskDeleteProgress> update)
    {
        lock (entry) entry.Progress = update(entry.Progress) with { UpdatedAt = DateTimeOffset.UtcNow };
    }
    private static string? BlockReason(AcquisitionTaskDbModel? task, AcquisitionDeletionActivity? state, bool retryRetainedFiles = false)
    {
        if (task == null) return "ACQUISITION_TASK_NOT_FOUND";
        if ((task.DeletedAt != null) != retryRetainedFiles)
            return retryRetainedFiles ? "CLEANUP_TASK_NOT_DELETED" : "ACQUISITION_TASK_DELETED";
        if (retryRetainedFiles && (task.Status != AcquisitionStatus.Completed || task.WorkflowRunId == null))
            return "CLEANUP_TASK_NOT_COMPLETED";
        if (task.WorkflowRunId == null && task.Status is (AcquisitionStatus.Running or AcquisitionStatus.Pending))
            return "ACQUISITION_TASK_ACTIVE";
        return state?.BlockReason ?? (state == null ? "ACQUISITION_EXECUTION_STATE_UNKNOWN" : null);
    }
    internal static string? RetainedPreviewReason(AcquisitionTaskDeleteCleanup cleanup)
    {
        if (cleanup.Groups.Count == 0 || cleanup.Groups.Any(g => g.Eligible)) return null;
        var onlyRetiredRecords = cleanup.FileCount == 0 && cleanup.Bytes == 0 &&
            cleanup.IdentifiedFileCount == 0 && cleanup.IdentifiedBytes == 0 &&
            cleanup.Groups.All(g => g.ReasonCode == "CLEANUP_RETIRED_WORKSPACE_REQUIRES_REVIEW" &&
                g.FileCount == 0 && g.Bytes == 0 && g.Files.Count == 0);
        return onlyRetiredRecords ? "CLEANUP_ONLY_RETIRED_WORKSPACE_RECORDS" : "CLEANUP_FILES_RETAINED";
    }

    private static bool AllowsRecordRemoval(string? reason) => reason is not ("CLEANUP_PATH_NOT_AUTHORIZED" or
        "CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW" or "ActiveOwner" or "ActiveConsumer" or
        "OperationNotCompleted" or "DeliveryAcceptanceChanged") &&
        reason?.StartsWith("ACQUISITION_", StringComparison.Ordinal) != true;
    internal static void ProtectOverlappingSelections(Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> plans)
    {
        var owners = new Dictionary<string, (int Task, string Artifact)>(ArtifactManifestFiles.PathComparer);
        var blocked = new HashSet<(int Task, string Artifact)>();
        foreach (var (task, plan) in plans)
            foreach (var group in plan.Summary.Groups)
                foreach (var file in group.Files)
                {
                    var owner = (task, group.ArtifactId);
                    if (owners.TryAdd(file.Path, owner) || owners[file.Path] == owner) continue;
                    if (owners[file.Path].Task == task && group.Kind == "publishedFiles" &&
                        SameDeliveredFile(plan, owners[file.Path].Artifact, group.ArtifactId, file.Path)) continue;
                    blocked.Add(owner);
                    blocked.Add(owners[file.Path]);
                }
        foreach (var task in plans.Keys.ToArray())
        {
            var plan = plans[task];
            var groups = plan.Summary.Groups.Select(g => blocked.Contains((task, g.ArtifactId))
                ? g with { Eligible = false, Shared = true, ReasonCode = g.ReasonCode ?? "SharedOwner" }
                : g with { Shared = g.Shared || g.Nodes.Count > 1 }).ToArray();
            var files = AcquisitionTaskDeletionCleanupService.Unique(groups.Where(g => g.Eligible).SelectMany(g => g.Files));
            plans[task] = plan with {
                Summary = plan.Summary with { Groups = groups, FileCount = files.Count, Bytes = files.Sum(f => f.Bytes) },
                Candidates = plan.Candidates.Where(c => !blocked.Contains((task, c.ArtifactId))).ToArray(),
                DeliveredFiles = plan.DeliveredFiles?.Where(c => !blocked.Contains((task, c.ArtifactId))).ToArray()
            };
        }

        static bool SameDeliveredFile(AcquisitionTaskDeletionCleanupService.Plan plan, string first, string second, string path)
        {
            if (plan.Consent?.DeleteDeliveredFiles != true) return false;
            var a = plan.DeliveredFiles?.SingleOrDefault(c => c.ArtifactId == first);
            var b = plan.DeliveredFiles?.SingleOrDefault(c => c.ArtifactId == second);
            return a?.FileIdentities?.TryGetValue(path, out var identity) == true &&
                b?.FileIdentities?.TryGetValue(path, out var other) == true && identity == other;
        }
    }
    private static string Version(AcquisitionTaskDbModel task, AcquisitionDeletionActivity state) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task) + "\n" + state.Version)));
}
