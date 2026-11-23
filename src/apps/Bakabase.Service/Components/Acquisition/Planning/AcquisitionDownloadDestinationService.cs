using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed class AcquisitionDownloadDestinationPreviewStore
{
    internal sealed record Target(CloudDownloadTarget Value, CloudDownloadDestinationPreview Preview, string RevisionId);
    internal sealed record TaskEntry(int RunId, string Version, string SemanticRoot, IReadOnlyList<Target> Targets);
    internal sealed record Entry(AcquisitionDownloadDestinationPreview Preview, string NewRoot,
        IReadOnlyDictionary<int, TaskEntry> Tasks)
    {
        internal bool Claimed;
        internal int[]? Selected;
        internal AcquisitionDownloadDestinationResult? Result;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries)
            if (pair.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow) _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 50) throw new ArgumentException("DOWNLOAD_DESTINATION_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewToken] = entry;
    }
    internal Entry Claim(string token, int[] selected)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var entry) ||
            entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("DOWNLOAD_DESTINATION_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (selected.Any(id => !entry.Tasks.ContainsKey(id))) throw new ArgumentException("DOWNLOAD_DESTINATION_SELECTION_INVALID");
            if (entry.Claimed && (entry.Result == null || !selected.SequenceEqual(entry.Selected!)))
                throw new ArgumentException("DOWNLOAD_DESTINATION_OUTCOME_UNKNOWN");
            entry.Claimed = true; entry.Selected = selected; return entry;
        }
    }
}

/// <summary>Revises only physical placement of existing downloads. Original plans, workflow checkpoints,
/// source identities, purchase evidence and transfer receipts remain immutable. Provider services own
/// durable per-download receipts; an interrupted batch must be reconciled through another preview.</summary>
public sealed class AcquisitionDownloadDestinationService(BakabaseDbContext db,
    ICloudDownloadDestinationService destinations, AcquisitionDownloadDestinationPreviewStore previews,
    IUserStoragePolicy storage, AcquisitionStoragePolicyDefaults defaults,
    FileProcessingWorkspaceResolver? workspaces = null)
{
    private sealed record State(AcquisitionTaskDbModel? Task, WorkflowRunDbModel? Run, DownloadWorkset? Workset,
        string? Problem, string Version);
    public async Task<AcquisitionDownloadDestinationPreview> PreviewAsync(AcquisitionDownloadDestinationPreviewInput input,
        Func<string, Task> guard, CancellationToken ct = default)
    {
        var ids = Ids(input.TaskIds);
        int? selectedSite = null;
        var reuseExistingFiles = input.ReuseExistingFiles == true;
        if (reuseExistingFiles || input.DownloadTaskIds != null)
        {
            if (!reuseExistingFiles || ids.Length != 1 || input.DownloadTaskIds is not {Count: 1} sites || sites[0] <= 0)
                throw new ArgumentException("DOWNLOAD_DESTINATION_SELECTION_INVALID");
            selectedSite = sites[0];
        }
        ValidateRoot(input.TargetRootDirectory);
        var root = Path.GetFullPath(input.TargetRootDirectory);
        await guard(root);
        // Explicit user preview captures current naming preferences once. They are never silently
        // refreshed at apply time, nor incorporated into an existing download's semantic identity.
        var policy = defaults.Capture();
        var rows = new List<AcquisitionDownloadDestinationRow>();
        var entries = new Dictionary<int, AcquisitionDownloadDestinationPreviewStore.TaskEntry>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ReadAsync(id, ct);
            var items = new List<AcquisitionDownloadDestinationItem>();
            var targets = new List<AcquisitionDownloadDestinationPreviewStore.Target>();
            var reason = state.Problem;
            if (reason == null)
            {
                var selected = state.Workset!.Targets.Where(t => !t.Excluded && (selectedSite == null || t.DownloadTaskId == selectedSite)).ToList();
                if (selectedSite != null && selected.Count != 1) throw new ArgumentException("DOWNLOAD_DESTINATION_SELECTION_INVALID");
                foreach (var target in selected)
                {
                    var intent = DownloadArtifactCoordinator.DownloadIntent(target, state.Workset.OutputDirectory, workspaces);
                    var directory = Path.Combine(root, ArtifactStoragePolicies.RenderDownload(policy,
                        target.Title ?? state.Task!.Title, intent, target.SelectedSource!.ProviderId));
                    await guard(directory);
                    var check = await destinations.PreviewAsync(state.Run!.Id, target, state.Workset.OutputDirectory,
                        root, directory, ct, reuseExistingFiles, guard);
                    if (check.PreviousDirectory != null) await guard(check.PreviousDirectory);
                    items.Add(new(check.DownloadTaskId ?? target.DownloadTaskId, check.PreviousDirectory, directory, check.CanRevise, check.ReasonCode,
                        check.CanPreviewReuse, check.ReuseEstimate));
                    if (check.CanRevise)
                        targets.Add(new(target, check, Guid.NewGuid().ToString("N")));
                    else
                        reason ??= check.ReasonCode;
                }
                if ((await ReadAsync(id, ct)).Version != state.Version)
                {
                    targets.Clear();
                    reason = "ACQUISITION_TASK_CHANGED";
                }
                if (targets.Count > 0)
                {
                    entries[id] = new(state.Run!.Id, state.Version, state.Workset.OutputDirectory, targets);
                    reason = targets.Count != items.Count ? "DOWNLOAD_DESTINATION_ELIGIBLE_PARTS" :
                        reuseExistingFiles ? "DOWNLOAD_DESTINATION_REUSE_READY" : "DOWNLOAD_DESTINATION_READY";
                }
            }
            rows.Add(new(id, state.Task?.Title, targets.Count > 0, reason ?? "DOWNLOAD_DESTINATION_CHECKPOINT_UNSUPPORTED", items));
        }
        var result = new AcquisitionDownloadDestinationPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            rows.Count, rows.SelectMany(r => r.Downloads).Select(d => d.DownloadTaskId).Where(d => d != null).Distinct().Count(), rows);
        previews.Add(new(result, root, entries));
        return result;
    }

    public async Task<AcquisitionDownloadDestinationResult> ApplyAsync(AcquisitionDownloadDestinationApplyInput input,
        Func<string, Task> guard, CancellationToken ct = default)
    {
        var ids = Ids(input.TaskIds).Order().ToArray();
        var entry = previews.Claim(input.PreviewToken, ids);
        if (entry.Result is {} saved) return saved;
        var applied = new List<int>(); var skipped = new List<AcquisitionDownloadDestinationSkip>(); var changed = 0;
        ValidateRoot(entry.NewRoot); await guard(entry.NewRoot);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var gate = AcquisitionTaskGate.For(id);
            if (!await gate.WaitAsync(0, ct)) {skipped.Add(new(id, "ACQUISITION_TASK_ACTIVE")); continue;}
            var count = 0;
            try
            {
                var expected = entry.Tasks[id];
                async Task ValidateOwner(CancellationToken token)
                {
                    var state = await ReadAsync(id, token);
                    if (state.Problem != null || state.Version != expected.Version)
                        throw new InvalidOperationException(state.Problem ?? "ACQUISITION_TASK_CHANGED");
                }
                await ValidateOwner(ct);
                // Revalidate every target selected by the preview before applying any. Each download repeats its own CAS
                // under operation/download/run gates; external edits can produce a partial batch,
                // which is reported explicitly rather than treated as an atomic task-wide success.
                foreach (var target in expected.Targets)
                {
                    if (target.Preview.PreviousDirectory != null) await guard(target.Preview.PreviousDirectory);
                    await guard(target.Preview.NewDirectory!);
                    var current = await destinations.PreviewAsync(expected.RunId, target.Value, expected.SemanticRoot,
                        entry.NewRoot, target.Preview.NewDirectory!, ct, target.Preview.ReuseEstimate != null, guard);
                    if (!current.CanRevise || current.Fingerprint != target.Preview.Fingerprint)
                        throw new InvalidOperationException(current.CanRevise ? "DOWNLOAD_DESTINATION_PREVIEW_CHANGED" : current.ReasonCode);
                }
                foreach (var target in expected.Targets)
                {
                    await destinations.ApplyAsync(expected.RunId, target.Value, expected.SemanticRoot, entry.NewRoot,
                        target.Preview.NewDirectory!, target.Preview.Fingerprint!, target.RevisionId, ct, ValidateOwner, target.Preview.ReuseEstimate != null, guard);
                    count++; changed++;
                }
                applied.Add(id);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                var code = error.Message;
                if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{2,100}$")) code = "DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE";
                skipped.Add(new(id, count > 0 ? "DOWNLOAD_DESTINATION_PARTIALLY_APPLIED" : code));
            }
            finally {gate.Release();}
        }
        var result = new AcquisitionDownloadDestinationResult(applied, skipped, changed);
        lock (entry) entry.Result = result;
        return result;
    }

    private void ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || root.Length > 4096 || root.Any(char.IsControl))
            throw new ArgumentException("DOWNLOAD_DESTINATION_ROOT_INVALID");
        AcquisitionOutputDirectoryResolver.ValidateConfiguredRoot(root, storage);
    }
    private async Task<State> ReadAsync(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var run = task?.WorkflowRunId is {} runId
            ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task) + "\n" + JsonSerializer.Serialize(run))));
        State Block(string reason) => new(task, run, null, reason, version);
        if (task == null || task.DeletedAt != null) return Block("ACQUISITION_TASK_NOT_FOUND");
        if (task.Status is AcquisitionStatus.Completed or AcquisitionStatus.Cancelled) return Block("DOWNLOAD_DESTINATION_TASK_CLOSED");
        if (run == null) return Block("DOWNLOAD_DESTINATION_NO_EXISTING_DOWNLOAD");
        if (run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending) return Block("ACQUISITION_TASK_ACTIVE");
        if (run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted) || run.PendingSignalJson != null)
            return Block("DOWNLOAD_DESTINATION_CHECKPOINT_UNSUPPORTED");
        try
        {
            var plan = WorkflowRunPlans.Read(run.PlanSnapshotJson);
            if (plan?.TriggerKind != CloudWorkflow.Trigger ||
                plan.Nodes.ElementAtOrDefault(run.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion")
                return Block("DOWNLOAD_DESTINATION_CHECKPOINT_UNSUPPORTED");
            var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
            var targets = workset?.Targets.Where(t => !t.Excluded).ToList();
            if (targets is not {Count: > 0 and <= 100} || targets.Any(t => t.SelectedSource == null || t.DownloadTaskId == null || t.AcquisitionNonce != null))
                return Block("DOWNLOAD_DESTINATION_NO_EXISTING_DOWNLOAD");
            if (targets.Select(t => t.DownloadTaskId).Distinct().Count() != targets.Count)
                return Block("DOWNLOAD_DESTINATION_SHARED_DOWNLOAD");
            return new(task, run, workset, null, version);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {return Block("DOWNLOAD_DESTINATION_CHECKPOINT_UNSUPPORTED");}
    }
    private static int[] Ids(IReadOnlyList<int>? ids)
    {
        if (ids == null || ids.Count is 0 or > 1000 || ids.Any(id => id <= 0)) throw new ArgumentException("DOWNLOAD_DESTINATION_SELECTION_INVALID");
        return ids.Distinct().ToArray();
    }
}
