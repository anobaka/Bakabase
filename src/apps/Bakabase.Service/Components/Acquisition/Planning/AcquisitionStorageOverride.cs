using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Bakabase.Service.Components.CloudDownloads;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionStorageOverridePreview(int TaskId, int WorkflowRunId, string Token,
    DateTimeOffset ExpiresAt, long MinimumFreeBytes, long OverrideMinimumFreeBytes, int DurationHours = 24,
    bool UseEmergencySpaceReserve = false, string EffectiveReserveMode = "configured", string Scope = "storage",
    IReadOnlyList<CloudDownloadResumeTarget>? DownloadTargets = null);
public sealed record AcquisitionStorageOverrideApplyInput(string Token, bool Confirmed, string? ReserveMode = null,
    bool? UseEmergencySpaceReserve = null, string? SignalJson = null);
public sealed record AcquisitionStorageOverrideResult(int TaskId, int WorkflowRunId, string Status,
    bool ContinuationAccepted, string? ReasonCode = null);

internal static class AcquisitionSpaceReserve
{
    internal const string Inherit = "inherit", Configured = "configured", Emergency = "emergency";

    internal static long Minimum(AcquisitionOptions settings, string mode = Inherit) => mode switch
    {
        Configured => settings.MinimumFreeSpaceBytes,
        Emergency => Math.Min(settings.MinimumFreeSpaceBytes, AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes),
        Inherit => settings.UseEmergencySpaceReserve ? Math.Min(settings.MinimumFreeSpaceBytes, AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes) : settings.MinimumFreeSpaceBytes,
        _ => throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID")
    };
}

/// <summary>Server-issued permission; never part of a plan, source identity, or global option.</summary>
internal sealed record AcquisitionStorageOverrideGrant(int WorkflowRunId, int PlanRevision, string Token,
    DateTimeOffset GrantedAt, DateTimeOffset ExpiresAt, string Status = "applied", string? ReasonCode = null,
    string ReserveMode = "emergency")
{
    internal const long EmergencyMinimumFreeBytes = 1024L * 1024 * 1024;
    internal static AcquisitionStorageOverrideGrant? Read(AcquisitionTaskDbModel task)
    {
        try
        {
            return string.IsNullOrWhiteSpace(task.BuilderStateJson) ? null :
                JsonNode.Parse(task.BuilderStateJson)?["storageOverride"]?.Deserialize<AcquisitionStorageOverrideGrant>(WorkflowJson.Options);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }
    internal bool IsCurrentBinding(AcquisitionTaskDbModel task, WorkflowRunDbModel run, DateTimeOffset now) =>
        Status == "applied" && GrantedAt <= now && task.DeletedAt == null &&
        task.WorkflowRunId == WorkflowRunId && run.Id == WorkflowRunId && task.PlanRevision == PlanRevision;
    internal bool IsValid(AcquisitionTaskDbModel task, WorkflowRunDbModel run, DateTimeOffset now) =>
        IsCurrentBinding(task, run, now) && ExpiresAt > now &&
        task.Status is not (AcquisitionStatus.Completed or AcquisitionStatus.Cancelled or AcquisitionStatus.Failed) &&
        run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running or WorkflowRunStatus.Waiting;
    internal static string WithState(string? json, AcquisitionStorageOverrideGrant grant)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("ACQUISITION_CHECKPOINT_INVALID");
        root["storageOverride"] = JsonSerializer.SerializeToNode(grant, WorkflowJson.Options);
        return root.ToJsonString(WorkflowJson.Options);
    }
}

public sealed class AcquisitionStorageOverridePreviewStore
{
    internal sealed record Entry(AcquisitionStorageOverridePreview Preview, string Version)
    {
        internal bool Claimed;
        internal string? Mode;
        internal string? Signal;
        internal AcquisitionStorageOverrideResult? Result;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries)
            if (pair.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow) _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 50) throw new ArgumentException("STORAGE_OVERRIDE_PREVIEW_LIMIT");
        _entries[entry.Preview.Token] = entry;
    }
    internal Entry Claim(int taskId, string token, string mode, string? signal = null)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var entry) ||
            entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("STORAGE_OVERRIDE_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (entry.Preview.TaskId != taskId || entry.Claimed && (entry.Mode != mode || entry.Signal != signal)) throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
            if (entry.Claimed && entry.Result == null) throw new ArgumentException("STORAGE_OVERRIDE_OUTCOME_UNKNOWN");
            entry.Claimed = true;
            entry.Mode = mode;
            entry.Signal = signal;
            return entry;
        }
    }
}

public sealed partial class AcquisitionStorageOverrideService(BakabaseDbContext db,
    IBOptions<AcquisitionOptions> options, AcquisitionStorageOverridePreviewStore previews,
    AcquisitionRecoveryService recovery, BTaskManager? tasks = null, AcquisitionRuntimeOverviewService? runtime = null,
    IStorageWritePolicyProvider? writePolicy = null, IWorkflowRunResumer? resumer = null,
    AcquisitionContinuationService? continuations = null, CloudManualDownloadResume? manualDownloads = null)
{
    public async Task<AcquisitionStorageOverridePreview> PreviewAsync(int id, CancellationToken ct = default, string scope = "storage")
    {
        var (task, run) = await CurrentAsync(id, ct);
        await ValidateAsync(task, run, ct, scope);
        var grant = AcquisitionStorageOverrideGrant.Read(task!);
        var currentMinimum = writePolicy != null ?
            await writePolicy.GetMinimumFreeBytesAsync(new("workflow", run!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), ct) :
            AcquisitionSpaceReserve.Minimum(options.Value, grant?.IsValid(task!, run!, DateTimeOffset.UtcNow) == true ? grant.ReserveMode : AcquisitionSpaceReserve.Inherit);
        var selection = scope == "download" && AcquisitionContinuationReserve.Kind(run) == "download.awaitCompletion"
            ? await (manualDownloads ?? new(db)).ReadMultipleAsync(run!.Id, AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson), ct)
            : null;
        var preview = new AcquisitionStorageOverridePreview(id, run!.Id, Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddMinutes(10), options.Value.MinimumFreeSpaceBytes,
            AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes, UseEmergencySpaceReserve: options.Value.UseEmergencySpaceReserve,
            EffectiveReserveMode: currentMinimum == AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes ? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Configured, Scope: scope, DownloadTargets: selection);
        previews.Add(new(preview, AcquisitionRecoveryService.Version(task!, run)));
        return preview;
    }

    public async Task<AcquisitionStorageOverrideResult> ApplyAsync(int id, AcquisitionStorageOverrideApplyInput input,
        CancellationToken ct = default, Func<string?, Task>? guardDirectory = null)
    {
        if (!input.Confirmed) throw new ArgumentException("STORAGE_OVERRIDE_CONFIRMATION_REQUIRED");
        var mode = input.UseEmergencySpaceReserve is { } useEmergency
            ? useEmergency ? AcquisitionSpaceReserve.Emergency : AcquisitionSpaceReserve.Configured
            : input.ReserveMode ?? AcquisitionSpaceReserve.Emergency;
        if (input.UseEmergencySpaceReserve != null && input.ReserveMode != null && input.ReserveMode != mode)
            throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        if (mode is not (AcquisitionSpaceReserve.Emergency or AcquisitionSpaceReserve.Configured))
            throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        var entry = previews.Claim(id, input.Token, mode, input.SignalJson);
        if (entry.Result is { } saved) return saved;
        if (entry.Preview.Scope == "download")
            return await ContinueDownloadAsync(id, input, entry, ct, guardDirectory);
        if (input.SignalJson != null) throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        var ownerGate = AcquisitionTaskGate.For(id);
        await ownerGate.WaitAsync(ct);
        AcquisitionStorageOverrideGrant grant;
        string grantedVersion;
        try
        {
            var runGate = WorkflowRunMutationGate.For(entry.Preview.WorkflowRunId);
            await runGate.WaitAsync(ct);
            try
            {
                if (entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
                    throw new ArgumentException("STORAGE_OVERRIDE_PREVIEW_EXPIRED");
                var (task, run) = await CurrentAsync(id, ct);
                await ValidateAsync(task, run, ct);
                if (AcquisitionRecoveryService.Version(task!, run) != entry.Version ||
                    options.Value.MinimumFreeSpaceBytes != entry.Preview.MinimumFreeBytes ||
                    options.Value.UseEmergencySpaceReserve != entry.Preview.UseEmergencySpaceReserve)
                    throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                var now = DateTimeOffset.UtcNow;
                grant = new(run!.Id, task!.PlanRevision, input.Token, now, now.AddHours(entry.Preview.DurationHours), ReserveMode: mode);
                await SaveAsync(task, grant, ct);
                grantedVersion = AcquisitionRecoveryService.Version(task with
                    {BuilderStateJson = AcquisitionStorageOverrideGrant.WithState(task.BuilderStateJson, grant)}, run);
            }
            finally { runGate.Release(); }
        }
        finally { ownerGate.Release(); }

        // The recovery service owns task -> run lock order and the complete file verification.
        // Never turn an override into a new plan, a forced download, or purchase permission.
        try
        {
            var (currentTask, currentRun) = await CurrentAsync(id, ct);
            if (currentTask == null || AcquisitionRecoveryService.Version(currentTask, currentRun) != grantedVersion)
                return await RevokeAsync(id, entry, grant, "ACQUISITION_TASK_CHANGED", ct);
            AcquisitionRecoveryResult result;
            if (currentRun?.CurrentStepIndex is >= 0 &&
                AcquisitionRecoveryService.IsLocalFileStep(WorkflowRunPlans.Read(currentRun.PlanSnapshotJson)?.Nodes
                    .ElementAtOrDefault(currentRun.CurrentStepIndex.Value)?.Kind))
                result = await recovery.ResumeGrantedFileStepAsync(id, grant.WorkflowRunId, grantedVersion, ct);
            else
            {
                var preview = await recovery.PreviewAsync(new([id], "workflow"), ct);
                var row = preview.Rows.Single();
                var (reviewedTask, reviewedRun) = await CurrentAsync(id, ct);
                if (reviewedTask == null || AcquisitionRecoveryService.Version(reviewedTask, reviewedRun) != grantedVersion)
                    return await RevokeAsync(id, entry, grant, "ACQUISITION_TASK_CHANGED", ct);
                if (!row.CanContinue || row.WorkflowRunId != grant.WorkflowRunId || row.Action != "resume")
                    return await RevokeAsync(id, entry, grant, row.ReasonCode, ct);
                result = await recovery.ApplyBatchAsync(new(preview.PreviewToken, [id]), null, ct);
            }
            if (!result.ContinuedTaskIds.Contains(id))
                return await RevokeAsync(id, entry, grant, result.Skipped.FirstOrDefault()?.ReasonCode ?? "RECOVERY_CHECK_INCOMPLETE", CancellationToken.None);
            var applied = new AcquisitionStorageOverrideResult(id, grant.WorkflowRunId, "applied", true);
            lock (entry) entry.Result = applied;
            return applied;
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException or ArgumentException)
        {
            // An enqueue may have committed before the exception. Keep the bound grant and
            // one-time claim; a duplicate request must never repeat continuation.
            var unknown = new AcquisitionStorageOverrideResult(id, grant.WorkflowRunId, "applied", false,
                "STORAGE_OVERRIDE_OUTCOME_UNKNOWN");
            lock (entry) entry.Result = unknown;
            return unknown;
        }
    }

    private async Task<AcquisitionStorageOverrideResult> RevokeAsync(int id, AcquisitionStorageOverridePreviewStore.Entry entry,
        AcquisitionStorageOverrideGrant grant, string reason, CancellationToken ct)
    {
        var ownerGate = AcquisitionTaskGate.For(id); await ownerGate.WaitAsync(ct);
        try
        {
            var runGate = WorkflowRunMutationGate.For(grant.WorkflowRunId); await runGate.WaitAsync(ct);
            try
            {
                var (task, _) = await CurrentAsync(id, ct);
                if (task != null && AcquisitionStorageOverrideGrant.Read(task)?.Token == grant.Token)
                    await SaveAsync(task, grant with {Status = "revoked", ReasonCode = reason}, ct);
            }
            finally { runGate.Release(); }
        }
        finally { ownerGate.Release(); }
        var result = new AcquisitionStorageOverrideResult(id, grant.WorkflowRunId, "revoked", false, reason);
        lock (entry) entry.Result = result;
        return result;
    }

    private async Task SaveAsync(AcquisitionTaskDbModel task, AcquisitionStorageOverrideGrant grant, CancellationToken ct)
    {
        var json = AcquisitionStorageOverrideGrant.WithState(task.BuilderStateJson, grant);
        var changed = await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.WorkflowRunId == task.WorkflowRunId &&
                t.PlanRevision == task.PlanRevision && t.DeletedAt == null && t.BuilderStateJson == task.BuilderStateJson)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct);
        if (changed != 1) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
        AcquisitionStorageWritePolicyProvider.Invalidate();
    }
    private async Task<(AcquisitionTaskDbModel?, WorkflowRunDbModel?)> CurrentAsync(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var run = task?.WorkflowRunId is { } runId ?
            await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        return (task, run);
    }
    private async Task ValidateAsync(AcquisitionTaskDbModel? task, WorkflowRunDbModel? run, CancellationToken ct, string scope = "storage")
    {
        if (scope is not ("storage" or "download")) throw new ArgumentException("STORAGE_OVERRIDE_SELECTION_INVALID");
        if (task == null || task.DeletedAt != null) throw new ArgumentException("ACQUISITION_TASK_NOT_FOUND");
        if (scope != "download" && options.Value.MinimumFreeSpaceBytes <= AcquisitionStorageOverrideGrant.EmergencyMinimumFreeBytes)
            throw new ArgumentException("STORAGE_OVERRIDE_NOT_NEEDED");
        if (task.Status != AcquisitionStatus.Waiting || task.PlanRevision < 1 || run == null ||
            run.Status != WorkflowRunStatus.Waiting || run.PendingSignalJson != null ||
            tasks?.IsPending("workflow.run." + run.Id) == true)
            throw new ArgumentException("STORAGE_OVERRIDE_TASK_NOT_WAITING");
        if (scope == "download")
        {
            if (!AcquisitionContinuationReserve.IsDownload(run)) throw new ArgumentException("STORAGE_OVERRIDE_REASON_UNSUPPORTED");
            return;
        }
        if (HasReserveLowReason(run)) return;
        // Download waits often keep the typed storage cause only on their current child.
        // Inspect the same read-only projection the task UI uses, never provider text or history.
        var overview = runtime == null ? null : (await runtime.GetAsync([task.Id], ct)).SingleOrDefault();
        if (overview?.WorkflowRunId != run.Id || overview.CurrentNodeKind != "download.awaitCompletion" ||
            overview.ActiveDownloadCount != 0 || overview.ProblemReasonCode != StorageWriteGuard.ReserveLowCode)
            throw new ArgumentException("STORAGE_OVERRIDE_REASON_UNSUPPORTED");
        var (currentTask, currentRun) = await CurrentAsync(task.Id, ct);
        if (currentTask == null || AcquisitionRecoveryService.Version(currentTask, currentRun) != AcquisitionRecoveryService.Version(task, run))
            throw new ArgumentException("ACQUISITION_TASK_CHANGED");
    }
    internal static bool HasReserveLowReason(WorkflowRunDbModel run)
    {
        if (LocalStorageFailures.PersistedCode(run.ErrorMessage) == StorageWriteGuard.ReserveLowCode) return true;
        if (string.IsNullOrWhiteSpace(run.WaitPromptJson) || run.WaitPromptJson.Length > 1024 * 1024) return false;
        try
        {
            using var prompt = JsonDocument.Parse(run.WaitPromptJson);
            return prompt.RootElement.ValueKind == JsonValueKind.Object &&
                prompt.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String &&
                LocalStorageFailures.PersistedCode(message.GetString()) == StorageWriteGuard.ReserveLowCode;
        }
        catch (JsonException) { return false; }
    }
}
