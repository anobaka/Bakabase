using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Delivery may close an obsolete, exclusively owned waiting revision. Explicit
/// acceptance or freshly verified complete delivery is required, together with the full revision chain.</summary>
public sealed class AcquisitionSupersededRunCleanup
{
    private readonly BakabaseDbContext db;
    private readonly IAcquisitionDeletionActivityReader activity;
    private readonly string receiptRoot;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal const string Reason = "ACQUISITION_SUPERSEDED_AFTER_ACCEPTED_DELIVERY";
    internal const string VerifiedDeliveryReason = "ACQUISITION_SUPERSEDED_AFTER_VERIFIED_DELIVERY";
    public AcquisitionSupersededRunCleanup(BakabaseDbContext db, IAcquisitionDeletionActivityReader activity, AppService app)
        : this(db, activity, app.AppDataDirectory) { }
    internal AcquisitionSupersededRunCleanup(BakabaseDbContext db, IAcquisitionDeletionActivityReader activity, string appData)
    { this.db = db; this.activity = activity; receiptRoot = Path.Combine(appData, "file-processing", "superseded-run-cleanup-receipts"); }

    internal sealed record Run(int RunId, int Revision, int ReplacedByRevision, string Version);
    internal sealed record Plan(string Id, int TaskId, int CompletedRunId, int CompletedRevision,
        [property: JsonIgnore] string HistoryJson, IReadOnlyList<Run> Runs, bool AbandonUnfinishedFiles = false, DateTime? DeletedAt = null)
    { public string HistoryFingerprint => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(HistoryJson))); }
    private sealed record PreviousRun(int RunId, WorkflowRunStatus Status, DateTime? CompletedAt);
    private sealed record Receipt(Plan Plan, string Reason, DateTime ConfirmedAt, string Status,
        IReadOnlyList<PreviousRun> OriginalRuns, IReadOnlyList<ArtifactAcceptedPublication>? VerifiedPublications = null);

    internal async Task<Plan?> PreviewAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners, CancellationToken ct, bool abandonUnfinishedFiles = false, bool reviewDeletedWaits = false)
    {
        var saved = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
        if (saved == null || saved.DeletedAt != task.DeletedAt || (saved.DeletedAt != null) != reviewDeletedWaits ||
            reviewDeletedWaits && !abandonUnfinishedFiles || saved.WorkflowRunId != task.WorkflowRunId ||
            saved.PlanRevision != task.PlanRevision || saved.PlanHistoryJson != task.PlanHistoryJson ||
            saved.PlanJson != task.PlanJson || saved.Status != task.Status) return null;
        var nonterminal = await db.WorkflowRuns.AsNoTracking().Where(r => owners.Contains(r.Id) &&
            (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting)).ToArrayAsync(ct);
        if (nonterminal.Length == 0) return new(Guid.NewGuid().ToString("N"), task.Id, task.WorkflowRunId ?? 0,
            task.PlanRevision, task.PlanHistoryJson ?? "[]", [], abandonUnfinishedFiles, saved.DeletedAt);
        var completed = task.WorkflowRunId ?? 0;
        if (!abandonUnfinishedFiles && completed == 0 ||
            nonterminal.Any(r => r.Status != WorkflowRunStatus.Waiting || r.PendingSignalJson != null)) return null;
        var live = (await activity.ReadAsync([task], ct)).GetValueOrDefault(task.Id);
        if (live?.BlockReason != null || live == null || !live.RunIds.Order().SequenceEqual(owners.Order())) return null;
        if (abandonUnfinishedFiles)
        {
            if (!live.CanAbandon(task, reviewDeletedWaits)) return null;
        }
        else if (task.Status != AcquisitionStatus.Completed || nonterminal.Any(r => r.Id == completed) ||
            !await db.WorkflowRuns.AnyAsync(r => r.Id == completed && r.Status == WorkflowRunStatus.Success && r.FailedItemCount == 0, ct))
            return null;
        List<AcquisitionPlanRevision>? history;
        AcquisitionPlan? plan;
        List<WorkflowSourceRef>? currentSources;
        try
        {
            history = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(task.PlanHistoryJson ?? "null", Json);
            plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", Json);
            currentSources = JsonSerializer.Deserialize<List<WorkflowSourceRef>>(task.SourceRefsJson ?? "[]", Json);
        }
        catch (JsonException) { return null; }
        // A whole-task deletion can close its own waiting revisions even before a final
        // publication. Exact source ownership and foreign claims are checked below.
        if (abandonUnfinishedFiles)
        {
            history ??= [];
            if (history.Any(h => h?.Plan?.Parts == null || h.SourceRefs == null)) return null;
        }
        else if (history is not {Count: > 0} || history.Count > 100 || plan?.Parts == null ||
            history.Any(h => h?.Plan?.Parts == null || h.SourceRefs == null) ||
            history.Any(h => h.Revision <= 0 || h.Revision > task.PlanRevision) || history.Select(h => h.Revision).Distinct().Count() != history.Count ||
            !history.Select(h => h.Revision).Order().SequenceEqual(Enumerable.Range(history.Min(h => h.Revision), history.Count)) ||
            history.Max(h => h.Revision) != task.PlanRevision ||
            JsonSerializer.Serialize(history.Single(h => h.Revision == task.PlanRevision).Plan, Json) != JsonSerializer.Serialize(plan, Json)) return null;
        var ids = nonterminal.Select(r => r.Id).ToArray();
        var taskKey = task.Id.ToString(CultureInfo.InvariantCulture);
        var allLinks = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => ids.Contains(s.WorkflowRunId) || s.WorkflowRunId == completed).ToArrayAsync(ct);
        var links = allLinks.Where(s => s.Module == "acquisition" && s.EntityKind == "task").ToArray();
        if (completed != 0 && !links.Any(s => s.WorkflowRunId == completed && s.EntityId == taskKey && s.Revision == task.PlanRevision.ToString(CultureInfo.InvariantCulture)) ||
            links.Any(s => s.EntityId != taskKey) ||
            await db.DownloadResultOwners.AnyAsync(o => ids.Contains(o.WorkflowRunId) && o.AcquisitionTaskId != task.Id, ct)) return null;
        if (reviewDeletedWaits)
        {
            var waitKeys = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var ownerKeys = owners.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var operations = db.ArtifactOperationBindings.Where(b => b.OwnerKind == "workflow" && waitKeys.Contains(b.OwnerId))
                .Select(b => b.OperationId);
            if (await db.ArtifactOperationBindings.AnyAsync(b => operations.Contains(b.OperationId) &&
                    (b.OwnerKind != "workflow" || !ownerKeys.Contains(b.OwnerId)), ct) ||
                await db.ArtifactConsumers.AnyAsync(c => operations.Contains(c.OperationId) && c.ReleasedAt == null &&
                    (c.OwnerKind != "workflow" || !ownerKeys.Contains(c.OwnerId!)) &&
                    (c.LeaseUntil > DateTime.UtcNow || c.OwnerKind == "workflow" && db.WorkflowRuns.Any(r =>
                        r.Id.ToString() == c.OwnerId && (r.Status == WorkflowRunStatus.Waiting ||
                        r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Pending))), ct)) return null;
        }
        // A post-parser source is provenance, not a second execution owner. Accept it
        // only when it appears in this task's frozen source history; unknown origins fail closed.
        var provenance = history.SelectMany(h => h.SourceRefs).Concat(currentSources ?? []).ToArray();
        if (allLinks.Where(s => ids.Contains(s.WorkflowRunId) && !(s.Module == "acquisition" && s.EntityKind == "task"))
            .Any(s => !provenance.Any(p => p.Module == s.Module && p.EntityKind == s.EntityKind && p.EntityId == s.EntityId &&
                (p.Revision ?? "") == s.Revision && (p.ResultKey ?? "") == s.ResultKey))) return null;
        // Historical task references count even when those tasks were removed from the list.
        var others = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id != task.Id)
            .Select(t => new {t.WorkflowRunId, t.PlanHistoryJson, t.PlanJson}).ToArrayAsync(ct);
        foreach (var other in others)
        {
            if (other.WorkflowRunId is { } id && ids.Contains(id)) return null;
            if (!AcquisitionDeletionActivityReader.ValidHistory(other.PlanHistoryJson)) return null;
            if (AcquisitionDeletionActivityReader.HistoryRuns(other.PlanHistoryJson).Any(ids.Contains)) return null;
            try
            {
                var otherPlans = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(other.PlanHistoryJson ?? "[]", Json) ?? [];
                var current = JsonSerializer.Deserialize<AcquisitionPlan>(other.PlanJson ?? "null", Json);
                if (otherPlans.Any(h => h?.Plan?.Parts == null) || current != null && current.Parts == null) return null;
                if (otherPlans.Select(h => h.Plan).Concat(current == null ? [] : new[] {current}).SelectMany(p => p.Parts)
                    .Any(p => p.ContinueFrom is { } input && ids.Contains(input.PreviousRunId))) return null;
            }
            catch (JsonException) { return null; }
        }
        var snapshots = new List<Run>();
        foreach (var run in nonterminal.OrderBy(r => r.Id))
        {
            var revisions = links.Where(s => s.WorkflowRunId == run.Id).Select(s => s.Revision).Distinct().ToArray();
            if (revisions.Length != 1 || !int.TryParse(revisions[0], out var revision) || revision > task.PlanRevision ||
                !abandonUnfinishedFiles && !history.Any(h => h.Revision == revision)) return null;
            if (abandonUnfinishedFiles && run.Id == completed && revision == task.PlanRevision)
            {
                snapshots.Add(new(run.Id, revision, revision, Version(run)));
                continue;
            }
            var replaced = history.Where(h => h.PreviousRunId == run.Id && h.Revision > revision).OrderBy(h => h.Revision).FirstOrDefault();
            if (!abandonUnfinishedFiles && replaced == null) return null;
            snapshots.Add(new(run.Id, revision, replaced?.Revision ?? revision, Version(run)));
        }
        return new(Guid.NewGuid().ToString("N"), task.Id, completed, task.PlanRevision, task.PlanHistoryJson ?? "[]", snapshots, abandonUnfinishedFiles, saved.DeletedAt);
    }

    internal async Task<bool> StillMatchesAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners, Plan expected, CancellationToken ct)
    {
        var current = await PreviewAsync(task, owners, ct, expected.AbandonUnfinishedFiles, expected.DeletedAt.HasValue);
        return current != null && current.DeletedAt == expected.DeletedAt && current.TaskId == expected.TaskId && current.CompletedRunId == expected.CompletedRunId &&
            current.CompletedRevision == expected.CompletedRevision && current.HistoryJson == expected.HistoryJson &&
            current.Runs.SequenceEqual(expected.Runs);
    }

    /// <remarks>Caller holds the task gate and all related WorkflowRunMutationGates.</remarks>
    internal Task<bool> ApplyAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners, Plan expected, CancellationToken ct) =>
        ApplyCoreAsync(task, owners, expected, expected.AbandonUnfinishedFiles ? "ACQUISITION_ABANDONED_BY_USER" : Reason, null, ct);

    /// <remarks>The delivery coordinator also holds the artifact maintenance fence and has
    /// freshly verified every required publication. These identities are durable audit evidence.</remarks>
    internal Task<bool> ApplyVerifiedDeliveryAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners, Plan expected,
        IReadOnlyList<ArtifactAcceptedPublication> verifiedPublications, CancellationToken ct)
    {
        if (verifiedPublications.Count == 0) throw new ArgumentException("Complete verified delivery is required.");
        return ApplyCoreAsync(task, owners, expected, VerifiedDeliveryReason, verifiedPublications, ct);
    }

    private async Task<bool> ApplyCoreAsync(AcquisitionTaskDbModel task, IReadOnlyList<int> owners, Plan expected,
        string reason, IReadOnlyList<ArtifactAcceptedPublication>? verifiedPublications, CancellationToken ct)
    {
        if (expected.Runs.Count == 0) return true;
        if (!await StillMatchesAsync(task, owners, expected, ct)) return false;
        var ids = expected.Runs.Select(r => r.RunId).ToArray();
        var original = await db.WorkflowRuns.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new PreviousRun(r.Id, r.Status, r.CompletedAt)).ToArrayAsync(ct);
        var receipt = new Receipt(expected, reason, DateTime.UtcNow, "Prepared", original, verifiedPublications);
        await SaveAsync(receipt, ct); // Durable intent before the transaction; never erase old node outputs or consumers.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await StillMatchesAsync(task, owners, expected, ct)) return false;
            var changed = await db.WorkflowRuns.Where(r => ids.Contains(r.Id) && r.Status == WorkflowRunStatus.Waiting && r.PendingSignalJson == null)
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled)
                    .SetProperty(r => r.CompletedAt, (DateTime?)DateTime.Now), ct);
            if (changed != ids.Length) return false;
            await transaction.CommitAsync(ct);
        }
        await SaveAsync(receipt with {Status = "Completed"}, ct);
        return true;
    }

    private async Task SaveAsync(Receipt receipt, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(receiptRoot);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(receiptRoot);
        else Directory.CreateDirectory(receiptRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var target = Path.Combine(receiptRoot, receipt.Plan.Id + ".json"); var pending = target + ".tmp";
        ArtifactManifestFiles.RejectLinks(target); ArtifactManifestFiles.RejectLinks(pending);
        var options = new FileStreamOptions {Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough};
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(pending, options))
        { await JsonSerializer.SerializeAsync(stream, receipt, Json, ct); await stream.FlushAsync(ct); stream.Flush(true); }
        File.Move(pending, target, true);
    }
    private static string Version(WorkflowRunDbModel run) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(run, Json)));
}
