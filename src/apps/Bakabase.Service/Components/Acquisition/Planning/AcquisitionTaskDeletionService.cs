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
using Bakabase.Service.Components.FileProcessing;
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
    internal static AcquisitionTaskDeleteOptions ForDirect(AcquisitionTaskDeleteInput input)
    {
        var files = input.CleanupFiles == true || input.DeleteDeliveredFiles == true;
        return new(input.CleanupFiles == true, files, files, input.RemoveTaskIfCleanupFails == true,
            input.RetryRetainedFiles == true, input.DeleteDeliveredFiles == true);
    }
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
        var deferredFinalDeletion = !preview.CleanupFiles && !preview.DeleteDeliveredFiles &&
            options.DeleteDeliveredFiles && options.AcceptDeliveredFiles && options.SkipFileVerification;
        if (options.CleanupFiles && !preview.CleanupFiles && !options.AcceptDeliveredFiles ||
            (preview.CleanupFiles || preview.DeleteDeliveredFiles) && (options.CleanupFiles != preview.CleanupFiles ||
                options.AcceptDeliveredFiles != preview.AcceptDeliveredFiles || options.SkipFileVerification != preview.SkipFileVerification) ||
            options.DeleteDeliveredFiles != preview.DeleteDeliveredFiles && !deferredFinalDeletion)
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
        if (SkipFileVerification && !HasFileCleanup) throw new ArgumentException("CLEANUP_ACCEPTANCE_REQUIRES_CLEANUP");
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
        internal Dictionary<int, (string Plan, AcquisitionDeletionWriters Writers)> Writers { get; init; } = new();
        public bool Claimed { get; set; }
        public AcquisitionTaskDeleteOptions? Options { get; set; }
        internal string? DirectRequestKey { get; init; }
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
        if (entry.DirectRequestKey == null) _entries[entry.Preview.PreviewId] = entry;
        else if (!_entries.TryAdd(entry.Preview.PreviewId, entry)) RequireDirectRequest(entry.Preview.PreviewId, entry.DirectRequestKey);
    }
    internal bool RequireDirectRequest(string id, string key)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
        if (!_entries.TryGetValue(id, out var entry)) return false;
        lock (entry)
            if (entry.DirectRequestKey != key) throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
        return true;
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
            if (entry.DirectRequestKey != null && input.TaskIds == null)
                throw new ArgumentException("ACQUISITION_DELETE_OPTIONS_CHANGED");
            var options = entry.DirectRequestKey != null ? AcquisitionTaskDeleteOptions.ForDirect(input) :
                AcquisitionTaskDeleteOptions.ForSubmit(entry.Preview, input);
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
    public Task<AcquisitionTaskDeletePreview> PreviewAsync(AcquisitionTaskDeletePreviewInput input, CancellationToken ct = default,
        Func<string, Task>? guardDirectory = null) => PrepareSelectionAsync(input, ct, guardDirectory);

    private async Task<AcquisitionTaskDeletePreview> PrepareSelectionAsync(AcquisitionTaskDeletePreviewInput input, CancellationToken ct,
        Func<string, Task>? guardDirectory, AcquisitionTaskDeleteInput? directRequest = null)
    {
        var started = Stopwatch.GetTimestamp();
        if (input.TaskIds == null || input.TaskIds.Count is 0 or > 10000 || input.TaskIds.Any(id => id <= 0))
            throw new ArgumentException("Choose between one and 10000 fixed acquisition task IDs.");
        var ids = input.TaskIds.Distinct().ToArray();
        var options = directRequest == null ? new AcquisitionTaskDeleteOptions(input.CleanupFiles == true, input.AcceptDeliveredFiles == true,
            input.SkipFileVerification == true, false, input.RetryRetainedFiles == true, input.DeleteDeliveredFiles == true) :
            AcquisitionTaskDeleteOptions.ForDirect(directRequest);
        options.Validate(ids.Length);
        if (options.HasFileCleanup && cleanup == null) throw new ArgumentException("CLEANUP_UNAVAILABLE");
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var states = await activity.ReadAsync(tasks.Where(t => (t.DeletedAt != null) == options.RetryRetainedFiles).ToArray(), ct);
        var writers = new Dictionary<int, (string Plan, AcquisitionDeletionWriters Writers)>();
        var rows = ids.Select(id =>
        {
            var task = tasks.SingleOrDefault(t => t.Id == id);
            var reason = BlockReason(task, states.GetValueOrDefault(id), options.RetryRetainedFiles);
            if (!options.RetryRetainedFiles && activity is IAcquisitionDeletionWriterControl &&
                reason is ("ACQUISITION_TASK_ACTIVE" or "ACQUISITION_DOWNLOAD_ACTIVE") &&
                states.GetValueOrDefault(id)?.Writers is {} owned && task is { DeletedAt: null })
            {
                writers.Add(id, (WriterPlan(task), owned));
                return new AcquisitionTaskDeleteRow(id, task.Title, true, null, WillStopExecution: true);
            }
            return new AcquisitionTaskDeleteRow(id, task?.Title, reason == null, reason, CanRemoveTaskIfCleanupFails: !options.RetryRetainedFiles && reason == null);
        }).ToArray();
        var cleanupPlans = new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>();
        if (options.HasFileCleanup && directRequest == null)
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (!row.CanDelete || row.WillStopExecution) continue;
                var preparing = Stopwatch.GetTimestamp();
                try
                {
                    var task = tasks.Single(t => t.Id == row.TaskId);
                    var owners = states[row.TaskId].RunIds;
                    var plan = await cleanup!.PreviewAsync(task, owners,
                        input.AcceptDeliveredFiles == true, input.SkipFileVerification == true, ct, guardDirectory, options.RetryRetainedFiles,
                        cleanupIntermediates: options.CleanupFiles, deleteDeliveredFiles: options.DeleteDeliveredFiles);
                    cleanupPlans[row.TaskId] = plan;
                    rows[i] = row with {Cleanup = plan.Summary, CanDelete = plan.Summary.ReasonCode == null,
                        ReasonCode = plan.Summary.ReasonCode, CanRemoveTaskIfCleanupFails = !options.RetryRetainedFiles && AllowsRecordRemoval(plan.Summary.ReasonCode)};
                }
                catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                { rows[i] = row with {CanDelete = false, CanRemoveTaskIfCleanupFails = false, ReasonCode = StorageBlockReason(error)}; }
                catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
                {
                    logger?.LogWarning(error, "Unable to preview cleanup for acquisition task {TaskId}", row.TaskId);
                    rows[i] = row with {CanDelete = false, ReasonCode = "CLEANUP_PREVIEW_FAILED"};
                }
                finally
                {
                    logger?.LogInformation("Acquisition cleanup preview for task {TaskId} took {ElapsedMs} ms. Skip content checks: {SkipContentChecks}; delete delivered files: {DeleteDeliveredFiles}; plan ready: {PlanReady}",
                        row.TaskId, Stopwatch.GetElapsedTime(preparing).TotalMilliseconds, options.SkipFileVerification,
                        options.DeleteDeliveredFiles, cleanupPlans.ContainsKey(row.TaskId));
                }
            }
        ProtectOverlappingSelections(cleanupPlans);
        rows = rows.Select(row =>
        {
            if (!cleanupPlans.TryGetValue(row.TaskId, out var plan)) return row;
            var retainedReason = (options.RetryRetainedFiles || plan.Consent?.AbandonUnfinishedFiles == true) && row.CanDelete
                ? RetainedPreviewReason(plan.Summary) : null;
            return row with {Cleanup = plan.Summary, CanDelete = row.CanDelete && retainedReason == null,
                ReasonCode = retainedReason ?? row.ReasonCode};
        }).ToArray();
        var uniqueFiles = AcquisitionTaskDeletionCleanupService.Unique(rows.Where(r => r.CanDelete)
            .SelectMany(r => r.Cleanup?.Groups.Where(g => g.Eligible).SelectMany(g => g.Files) ?? []));
        var identifiedFiles = AcquisitionTaskDeletionCleanupService.Unique(rows.SelectMany(r => r.Cleanup?.Groups.SelectMany(g => g.Files) ?? []));
        rows = rows.Select(row => row.Cleanup == null ? row : row with {Cleanup = row.Cleanup with
            {Groups = row.Cleanup.Groups.Select(g => g with {Files = g.Files.Take(200).ToArray(), FilesTruncated = g.Files.Count > 200}).ToArray()}}).ToArray();
        var ready = rows.Where(r => r.CanDelete).Select(r => r.TaskId).ToHashSet();
        var preview = new AcquisitionTaskDeletePreview(directRequest?.PreviewId ?? Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            ids.Length, ready.Count, ids.Length - ready.Count, rows, options.CleanupFiles,
            input.AcceptDeliveredFiles == true, uniqueFiles.Count, uniqueFiles.Sum(f => f.Bytes), identifiedFiles.Count,
            identifiedFiles.Sum(f => f.Bytes), options.HasFileCleanup && rows.Any(r => r.Cleanup?.SizeIncomplete != false),
            input.SkipFileVerification == true, options.RetryRetainedFiles, options.DeleteDeliveredFiles);
        var confirmed = rows.Where(r => r.CanDelete || r.CanRemoveTaskIfCleanupFails).Select(r => r.TaskId).ToHashSet();
        previews.Add(new(preview, tasks.Where(t => confirmed.Contains(t.Id)).ToDictionary(t => t.Id, t => Version(t, states[t.Id])),
            states.Where(s => confirmed.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value.RunIds), cleanupPlans)
            { Writers = writers, DirectRequestKey = directRequest == null ? null : DirectRequestKey(directRequest) });
        logger?.LogInformation("Acquisition deletion preview for {TaskCount} tasks took {ElapsedMs} ms. File cleanup requested: {CleanupRequested}; skip content checks: {SkipContentChecks}",
            ids.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds, options.HasFileCleanup, options.SkipFileVerification);
        return preview;
    }

    public async Task<AcquisitionTaskDeleteResult> DeleteAsync(AcquisitionTaskDeleteInput input, CancellationToken ct = default,
        Func<string, Task>? guardDirectory = null)
    {
        if (input.TaskIds != null)
        {
            AcquisitionTaskDeleteOptions.ForDirect(input).Validate(input.TaskIds.Distinct().Count());
            if (!previews.RequireDirectRequest(input.PreviewId, DirectRequestKey(input)))
                // Only task identities and activity are read here. Files are resolved once,
                // after confirmation and under the task/run mutation gates.
                await PrepareSelectionAsync(new(input.TaskIds, RetryRetainedFiles: input.RetryRetainedFiles),
                    ct, guardDirectory, input);
        }
        var entry = previews.Claim(input);
        if (entry.Result is {} saved)
        {
            // Another session may hold the same idempotency key. Re-authorize paths in the response.
            foreach (var path in (saved.CleanupResults ?? [])
                         .SelectMany(r => (r.RetainedDirectories ?? []).Concat(r.RemovedDirectories ?? [])).Distinct())
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
        var accessBlocks = new Dictionary<int, string>();
        var plans = entry.CleanupPlans?.ToDictionary(p => p.Key, p => p.Value) ?? new();
        var versions = entry.Versions.ToDictionary(p => p.Key, p => p.Value);
        try
        {
            // A confirmation can arrive through a different remote session. Authorize the
            // whole fixed selection before cancelling an obsolete run or unlinking any file.
            if ((entry.Preview.CleanupFiles || entry.Preview.DeleteDeliveredFiles) && cleanup != null)
                foreach (var (id, plan) in entry.CleanupPlans ?? new Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan>())
                {
                    try { await cleanup.EnsureAuthorizedAsync(plan, guardDirectory, ct); }
                    catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                    { accessBlocks[id] = StorageBlockReason(error); }
                }
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
            // A running acquisition is stopped only after the same final confirmation.
            // A live inventory is unstable. Snapshot it after all of its writers have stopped.
            foreach (var (id, _) in taskLocks.Where(t => entry.Writers.ContainsKey(t.Id)))
            {
                Report(entry, p => p with { CurrentTaskId = id, Phase = "rechecking" });
                var task = await db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
                var live = (await activity.ReadAsync([task], ct))[id];
                var expected = entry.Writers[id];
                if (entry.Runs[id].Any(blockedRuns.Contains))
                { accessBlocks[id] = "ACQUISITION_TASK_ACTIVE"; continue; }
                if (task.DeletedAt != null || WriterPlan(task) != expected.Plan || !entry.Runs[id].SequenceEqual(live.RunIds))
                { accessBlocks[id] = "ACQUISITION_TASK_CHANGED"; continue; }
                if (live.BlockReason == null) continue; // The task finished between preview and confirmation.
                if (live.Writers is not {} writing || !expected.Writers.Covers(writing))
                { accessBlocks[id] = live.BlockReason; continue; }
                using var stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stopping.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    if (options.HasFileCleanup && cleanup != null)
                        await cleanup.AuthorizeWriterDirectoriesAsync(task, live.RunIds, options.CleanupFiles,
                            options.DeleteDeliveredFiles, guardDirectory, ct);
                    await ((IAcquisitionDeletionWriterControl)activity).StopWritersAsync(writing, stopping.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { accessBlocks[id] = "ACQUISITION_TASK_ACTIVE"; }
                catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                { accessBlocks[id] = StorageBlockReason(error); }
                catch (Exception error) when (error is InvalidOperationException or System.IO.IOException or ArgumentException or JsonException)
                { logger?.LogWarning(error, "Could not stop acquisition {TaskId} for deletion", id); accessBlocks[id] = "ACQUISITION_TASK_ACTIVE"; }
            }
            var ids = taskLocks.Select(t => t.Id).ToArray();
            var current = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            var states = await activity.ReadAsync(current.Values.Where(t => (t.DeletedAt != null) == options.RetryRetainedFiles).ToArray(), ct);
            foreach (var id in ids.Where(entry.Writers.ContainsKey))
            {
                if (current.TryGetValue(id, out var task) && states.TryGetValue(id, out var state) &&
                    entry.Writers[id].Plan == WriterPlan(task) && entry.Runs[id].SequenceEqual(state.RunIds))
                    versions[id] = Version(task, state);
                else accessBlocks[id] = "ACQUISITION_TASK_CHANGED";
            }
            string? CheckTask(int id)
            {
                if (accessBlocks.TryGetValue(id, out var accessBlock)) return accessBlock;
                var task = current.GetValueOrDefault(id);
                var state = states.GetValueOrDefault(id);
                if (BlockReason(task, state, options.RetryRetainedFiles) is {} reason) return reason;
                if (entry.Runs[id].Any(blockedRuns.Contains)) return "ACQUISITION_TASK_ACTIVE";
                return Version(task!, state!) != versions[id] ? "ACQUISITION_TASK_CHANGED" : null;
            }
            if (options.HasFileCleanup && entry.DirectRequestKey == null &&
                (entry.Writers.Count > 0 || !entry.Preview.CleanupFiles && !entry.Preview.DeleteDeliveredFiles))
            {
                // The user confirmed the task IDs and cleanup policy without opening an inventory.
                // Resolve the whole fixed selection in their current access scope before any mutation.
                if (cleanup == null) throw new ArgumentException("CLEANUP_UNAVAILABLE");
                foreach (var id in ids.Where(id => (entry.Writers.ContainsKey(id) || !entry.Preview.CleanupFiles && !entry.Preview.DeleteDeliveredFiles) && CheckTask(id) == null))
                {
                    Report(entry, p => p with { CurrentTaskId = id, Phase = "rechecking" });
                    var preparing = Stopwatch.GetTimestamp();
                    try
                    {
                        var plan = await cleanup.PreviewAsync(current[id], entry.Runs[id], options.AcceptDeliveredFiles,
                            options.SkipFileVerification, ct, guardDirectory, options.RetryRetainedFiles,
                            cleanupIntermediates: options.CleanupFiles, deleteDeliveredFiles: options.DeleteDeliveredFiles,
                            directDirectoryDeletion: entry.DirectRequestKey != null);
                        plans[id] = plan;
                    }
                    catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                    { accessBlocks[id] = StorageBlockReason(error); }
                    catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
                    { /* No file plan or paths are exposed. Record removal still requires current task safety. */ }
                    finally
                    {
                        logger?.LogInformation("Acquisition cleanup preparation for task {TaskId} took {ElapsedMs} ms. Skip content checks: {SkipContentChecks}; plan ready: {PlanReady}",
                            id, Stopwatch.GetElapsedTime(preparing).TotalMilliseconds, options.SkipFileVerification, plans.ContainsKey(id));
                    }
                }
                ProtectOverlappingSelections(plans);
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
                        if (entry.DirectRequestKey != null)
                        {
                            try
                            {
                                // Resolve and delete one task at a time. A large fixed selection
                                // never has to wait for every directory before its first removal.
                                plans[id] = await cleanup!.PreviewAsync(task!, entry.Runs[id], true, true, ct,
                                    guardDirectory, options.RetryRetainedFiles, options.CleanupFiles,
                                    options.DeleteDeliveredFiles, directDirectoryDeletion: true);
                            }
                            catch (Exception error) when (error is UnauthorizedAccessException or UserStoragePathException)
                            {
                                skipped.Add(new(id, StorageBlockReason(error)));
                                taskFinished = true;
                                continue;
                            }
                            catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or ArgumentException or JsonException)
                            {
                                logger?.LogWarning(error, "Could not resolve acquisition directories for task {TaskId}", id);
                            }
                        }
                        var plan = plans.GetValueOrDefault(id);
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
                            if (block == null && (Version(latestTask!, states[id]) != versions[id] ||
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
                cleanupResults.Sum(r => r.RemovedFiles), cleanupResults.Sum(r => r.RemovedBytes),
                cleanupResults.SelectMany(r => r.RemovedDirectories ?? []).Distinct(ArtifactManifestFiles.PathComparer).ToArray(),
                cleanupResults.Any(r => r.FileStatisticsIncomplete));
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
    private static string DirectRequestKey(AcquisitionTaskDeleteInput input) => JsonSerializer.Serialize(new
        { TaskIds = input.TaskIds!.Distinct().Order().ToArray(), Options = AcquisitionTaskDeleteOptions.ForDirect(input) });
    private static string StorageBlockReason(Exception error) =>
        error is UserStoragePathException {MountUnavailable: true} ? "WorkspaceUnavailable" : "CLEANUP_PATH_NOT_AUTHORIZED";
    private static string? BlockReason(AcquisitionTaskDbModel? task, AcquisitionDeletionActivity? state, bool retryRetainedFiles = false)
    {
        if (task == null) return "ACQUISITION_TASK_NOT_FOUND";
        if ((task.DeletedAt != null) != retryRetainedFiles)
            return retryRetainedFiles ? "CLEANUP_TASK_NOT_DELETED" : "ACQUISITION_TASK_DELETED";
        if (task.WorkflowRunId == null && task.Status == AcquisitionStatus.Running)
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

    private static bool AllowsRecordRemoval(string? reason) => reason is not ("CLEANUP_TASK_NOT_COMPLETED" or "CLEANUP_PATH_NOT_AUTHORIZED" or
        "CLEANUP_OLD_EXECUTION_REQUIRES_REVIEW" or "ActiveOwner" or "ActiveConsumer" or
        "OperationNotCompleted" or "DeliveryAcceptanceChanged") &&
        reason?.StartsWith("ACQUISITION_", StringComparison.Ordinal) != true;
    internal static void ProtectOverlappingSelections(Dictionary<int, AcquisitionTaskDeletionCleanupService.Plan> plans)
    {
        foreach (var task in plans.Keys.ToArray())
            if (plans[task].Consent?.TaskId == task)
                plans[task] = CoalesceContainedPrivateSelections(CoalesceIdenticalPrivateSelections(plans[task]));
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
                DeliveredFiles = plan.DeliveredFiles?.Where(c => !blocked.Contains((task, c.ArtifactId))).ToArray(),
                AbandonedInputs = plan.AbandonedInputs?.Where(c => !blocked.Contains((task, c.Group.ArtifactId))).ToArray(),
                CapturedInputs = plan.CapturedInputs?.Where(c => !blocked.Contains((task, c.Group.ArtifactId))).ToArray(),
                StageDirectories = plan.StageDirectories?.Where(c => !blocked.Contains((task, c.Id))).ToArray()
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

    private static AcquisitionTaskDeletionCleanupService.Plan CoalesceIdenticalPrivateSelections(
        AcquisitionTaskDeletionCleanupService.Plan plan)
    {
        if (plan.Consent is not {DeliveryAccepted: true, SkipFileVerification: true}) return plan;
        var candidates = plan.Candidates.Where(c => c.PublishedArtifactId == null).ToLookup(c => c.ArtifactId, StringComparer.Ordinal);
        var groups = new List<AcquisitionTaskDeleteCleanupGroup>();
        var firstBySelection = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in plan.Summary.Groups)
        {
            var matching = candidates[group.ArtifactId].ToArray();
            var candidate = matching.Length == 1 ? matching[0] : null;
            var fingerprint = candidate?.PrivateSelectionFingerprint;
            if (group is not {Kind: "intermediate", Eligible: true, SizeUnknown: false, FilesTruncated: false} ||
                group.Files.Count == 0 || candidate is not {Eligible: true, PreviewError: null} ||
                fingerprint is not {Length: 64} || !fingerprint.All(Uri.IsHexDigit) ||
                !SamePrivateFiles(group.Files, candidate.PreviewFiles))
            { groups.Add(group); continue; }
            if (!firstBySelection.TryGetValue(fingerprint, out var index))
            { firstBySelection.Add(fingerprint, groups.Count); groups.Add(group); continue; }
            var first = groups[index];
            if (first.ArtifactId == group.ArtifactId || !SamePrivateFiles(first.Files, candidate.PreviewFiles))
            { groups.Add(group); continue; }
            // Both independent server-side proofs selected exactly the same paths,
            // lengths and modification times for this fixed task. Keep one original
            // execution proof and all node attribution, not two competing deletions.
            groups[index] = first with {Nodes = first.Nodes.Concat(group.Nodes).Distinct().ToArray()};
            duplicates.Add(group.ArtifactId);
        }
        if (duplicates.Count == 0) return plan;
        return plan with {Summary = plan.Summary with {Groups = groups},
            Candidates = plan.Candidates.Where(c => c.PublishedArtifactId != null || !duplicates.Contains(c.ArtifactId)).ToArray()};

        static bool SamePrivateFiles(IReadOnlyList<AcquisitionTaskDeleteCleanupFile> selected,
            IReadOnlyList<(string Path, long Bytes)>? recorded)
        {
            if (recorded == null || selected.Count != recorded.Count) return false;
            var ordered = selected.OrderBy(f => f.Path, ArtifactManifestFiles.PathComparer).ToArray();
            var proof = recorded.OrderBy(f => f.Path, ArtifactManifestFiles.PathComparer).ToArray();
            return ordered.Select((file, i) => file.Bytes == proof[i].Bytes &&
                ArtifactManifestFiles.PathComparer.Equals(file.Path, proof[i].Path)).All(equal => equal);
        }
    }

    private static AcquisitionTaskDeletionCleanupService.Plan CoalesceContainedPrivateSelections(
        AcquisitionTaskDeletionCleanupService.Plan plan)
    {
        if (plan.Consent is not {DeliveryAccepted: true, SkipFileVerification: true}) return plan;
        var candidates = plan.Candidates.Where(c => c.PublishedArtifactId == null)
            .ToLookup(c => c.ArtifactId, StringComparer.Ordinal);
        var groups = plan.Summary.Groups.ToArray();
        var proven = new Dictionary<int, (ArtifactWorkspaceCleanupPreview Candidate,
            Dictionary<string, long> Files, string Checkpoint)>();
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var matching = candidates[group.ArtifactId].ToArray();
            var candidate = matching.Length == 1 ? matching[0] : null;
            if (group is not {Kind: "intermediate", Eligible: true, SizeUnknown: false, FilesTruncated: false} ||
                group.Files.Count == 0 || group.Files.Count != group.FileCount ||
                candidate is not {Eligible: true, PreviewError: null, PreviewFiles: not null,
                    PrivateSelectionFingerprint: {Length: 64} fingerprint} || !fingerprint.All(Uri.IsHexDigit) ||
                candidate.PreviewFiles.Count != group.Files.Count) continue;
            var selected = group.Files.DistinctBy(f => f.Path, ArtifactManifestFiles.PathComparer)
                .ToDictionary(f => f.Path, f => f.Bytes, ArtifactManifestFiles.PathComparer);
            if (selected.Count != group.Files.Count || group.Bytes != selected.Values.Sum() ||
                candidate.PreviewFiles.Select(f => f.Path).Distinct(ArtifactManifestFiles.PathComparer).Count() != selected.Count ||
                candidate.PreviewFiles.Any(f => !selected.TryGetValue(f.Path, out var bytes) || bytes != f.Bytes)) continue;
            var checkpoint = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(candidate.ResultDirectory));
            if (checkpoint == null || selected.Keys.Any(path => !ArtifactCleanupPathReferences.StrictlyUnder(path, checkpoint))) continue;
            proven.Add(index, (candidate, selected, checkpoint));
        }
        var removed = new HashSet<int>();
        var duplicateArtifacts = new HashSet<string>(StringComparer.Ordinal);
        // Keep an existing maximal proof. Never create a union, a new fingerprint or
        // an edited execution selection from the presentation rows.
        foreach (var larger in proven.Keys.OrderByDescending(index => proven[index].Files.Count))
        {
            if (removed.Contains(larger)) continue;
            var first = proven[larger];
            foreach (var smaller in proven.Keys)
            {
                if (larger == smaller || removed.Contains(smaller)) continue;
                var second = proven[smaller];
                if (first.Candidate.ArtifactId == second.Candidate.ArtifactId ||
                    first.Files.Count <= second.Files.Count ||
                    !ArtifactManifestFiles.PathComparer.Equals(first.Checkpoint, second.Checkpoint) ||
                    second.Files.Any(file => !first.Files.TryGetValue(file.Key, out var bytes) || bytes != file.Value)) continue;
                // Both private selections already passed their own ownership, references
                // and executor-ledger checks. Sharing this exact checkpoint is an extra
                // restriction, not permission inferred from directory names.
                groups[larger] = groups[larger] with
                    {Nodes = groups[larger].Nodes.Concat(groups[smaller].Nodes).Distinct().ToArray()};
                removed.Add(smaller);
                duplicateArtifacts.Add(second.Candidate.ArtifactId);
            }
        }
        return removed.Count == 0 ? plan : plan with
        {
            Summary = plan.Summary with {Groups = groups.Where((_, index) => !removed.Contains(index)).ToArray()},
            Candidates = plan.Candidates.Where(c => c.PublishedArtifactId != null ||
                !duplicateArtifacts.Contains(c.ArtifactId)).ToArray()
        };
    }
    // Executing nodes legitimately change status/progress. The task, its sources, plan and
    // directories must still be exactly the ones the user confirmed.
    private static string WriterPlan(AcquisitionTaskDbModel task) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new {task.Id, task.WorkflowRunId, task.PlanRevision, task.PlanJson,
            task.PlanHistoryJson, task.Title, task.PreparedContextJson, task.SourceRefsJson, task.BuilderId,
            task.TargetDirectory, task.ResourceId, task.LeadKind, task.LeadValue})));
    private static string Version(AcquisitionTaskDbModel task, AcquisitionDeletionActivity state) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task) + "\n" + state.Version)));
}
