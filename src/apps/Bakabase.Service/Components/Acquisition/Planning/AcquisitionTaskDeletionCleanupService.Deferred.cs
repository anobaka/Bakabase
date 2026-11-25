using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static class DeferredStageCleanup
{
    // A per-scope tombstone points at the exact private receipts confirmed by this user.
    // Historical plain incomplete/unknown states carry no background deletion authority.
    internal const string Prefix = "smbPending:", PendingReason = "CLEANUP_SMB_DELETE_PENDING";
    internal const string AwaitRemoval = "CLEANUP_SMB_AWAITING_TASK_REMOVAL";
    internal const string RequiresReview = "CLEANUP_FILES_RETAINED";
    private const string WithManualReview = "review:";
    internal static bool Pending(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    internal static string DisplayStatus(string? value) => Pending(value) ? AcquisitionTaskDeletionService.IncompleteCleanup : value ?? "unknown";
    internal static bool IsAwaitingFileRelease(string? status)
    {
        if (!Pending(status)) return false;
        var (ids, review) = Decode(status!);
        return !review && ids.Length > 0;
    }
    internal static string[] AwaitingFileReleaseScopes(string? intermediate, string? delivery)
    {
        var scopes = new List<string>(2);
        if (IsAwaitingFileRelease(intermediate)) scopes.Add("intermediate");
        if (IsAwaitingFileRelease(delivery)) scopes.Add("delivery");
        return scopes.ToArray();
    }
    internal static bool HasPendingRemovalReceipts(string? intermediate, string? delivery)
    {
        var pending = false;
        foreach (var scope in new[] {intermediate, delivery})
        {
            if (scope is AcquisitionTaskDeletionService.CompleteCleanup or AcquisitionTaskDeletionService.NoCleanup) continue;
            if (!IsAwaitingFileRelease(scope)) return false;
            pending = true;
        }
        return pending;
    }
    internal static string[] DisplayReasons(string? intermediate, string? delivery, string? saved)
    {
        var codes = saved?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return Pending(intermediate) || Pending(delivery)
            ? codes.Where(c => c != PendingReason).Append("CLEANUP_SMB_AUTO_PENDING").Distinct().ToArray() : codes;
    }
    internal static string Status(AcquisitionTaskDeleteCleanupOutcome? result, AcquisitionCleanupScopes scope)
    {
        if (result == null) return AcquisitionTaskDeletionService.IncompleteCleanup;
        var ids = result.DeferredReceipts.Where(r => (r.Scopes & scope) != 0).Select(r => r.Id).Distinct().Order().ToArray();
        return ids.Length == 0 ? AcquisitionTaskDeletionService.IncompleteCleanup : Encode(ids, (result.ManualReviewScopes & scope) != 0);
    }
    internal static string Encode(string[] ids, bool review) => Prefix + (review ? WithManualReview : "") + string.Join(',', ids);
    internal static (string[] Ids, bool Review) Decode(string status)
    {
        var value = status[Prefix.Length..];
        var review = value.StartsWith(WithManualReview, StringComparison.Ordinal);
        var ids = (review ? value[WithManualReview.Length..] : value).Split(',');
        return ids.All(id => id.Length == 32 && Guid.TryParseExact(id, "N", out _)) ? (ids, review) : ([], true);
    }
}

public sealed partial class AcquisitionTaskDeletionCleanupService
{
    internal async Task<int> FinishPendingSmbBatchAsync(int afterId, CancellationToken ct)
    {
        var ids = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id > afterId && (t.DeletedAt != null || t.DeletedCleanupReasons == DeferredStageCleanup.AwaitRemoval) &&
                (t.DeletedIntermediateCleanup != null && t.DeletedIntermediateCleanup.StartsWith(DeferredStageCleanup.Prefix) ||
                 t.DeletedDeliveryCleanup != null && t.DeletedDeliveryCleanup.StartsWith(DeferredStageCleanup.Prefix)))
            .OrderBy(t => t.Id).Select(t => t.Id).Take(8).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            try { await FinishPendingSmbAsync(id, ct); }
            catch (Exception error) when (!ct.IsCancellationRequested)
            { logger?.LogWarning(error, "Cannot finish pending SMB cleanup for acquisition {TaskId}.", id); }
        }
        return ids.Length == 8 ? ids[^1] : 0;
    }

    internal async Task FinishPendingSmbAsync(int taskId, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(taskId);
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId &&
                (t.DeletedAt != null || t.DeletedCleanupReasons == DeferredStageCleanup.AwaitRemoval), ct);
            if (task == null) return;
            var needsReview = false;
            async Task<string?> FinishScope(string? status, AcquisitionCleanupScopes scope)
            {
                if (!DeferredStageCleanup.Pending(status)) return status;
                var (ids, review) = DeferredStageCleanup.Decode(status!);
                if (ids.Length == 0) needsReview = true;
                var waiting = new List<string>();
                foreach (var id in ids)
                {
                    // A shared parent can cover both confirmed scopes, but a different receipt
                    // or a stored scope that does not match it can never expand its selection.
                    var state = await FinishSmbReceiptAsync(task, id, scope, ct);
                    if (state == DeferredEmptyDirectoryResult.Waiting) waiting.Add(id);
                    if (state == DeferredEmptyDirectoryResult.Changed) { review = true; needsReview = true; }
                }
                if (waiting.Count != 0) return DeferredStageCleanup.Encode(waiting.ToArray(), review);
                return review ? AcquisitionTaskDeletionService.IncompleteCleanup : AcquisitionTaskDeletionService.CompleteCleanup;
            }
            var intermediate = await FinishScope(task.DeletedIntermediateCleanup, AcquisitionCleanupScopes.Intermediate);
            var delivery = await FinishScope(task.DeletedDeliveryCleanup, AcquisitionCleanupScopes.Delivery);
            if (intermediate == task.DeletedIntermediateCleanup && delivery == task.DeletedDeliveryCleanup) return;
            var reasons = (task.DeletedCleanupReasons ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (!DeferredStageCleanup.Pending(intermediate) && !DeferredStageCleanup.Pending(delivery)) reasons.Remove(DeferredStageCleanup.PendingReason);
            if (needsReview) reasons.Add(DeferredStageCleanup.RequiresReview);
            var deletedAt = task.DeletedAt;
            var codes = string.Join(',', reasons.Order(StringComparer.Ordinal));
            if (deletedAt == null)
            {
                static bool Done(string? value) => value is AcquisitionTaskDeletionService.CompleteCleanup or AcquisitionTaskDeletionService.NoCleanup;
                if (needsReview || intermediate == AcquisitionTaskDeletionService.IncompleteCleanup || delivery == AcquisitionTaskDeletionService.IncompleteCleanup)
                    codes = DeferredStageCleanup.RequiresReview;
                else if (Done(intermediate) && Done(delivery))
                {
                    var owners = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking()
                        .Where(s => s.Module == "acquisition" && s.EntityKind == "task" && s.EntityId == taskId.ToString())
                        .Select(s => s.WorkflowRunId).Distinct().ToArrayAsync(ct);
                    // Another selected stage may have reappeared while the client held the last file.
                    // Recheck the entire selection without deleting any newly arrived contents.
                    var check = await PreviewAsync(task, owners, true, true, ct,
                        cleanupIntermediates: intermediate == AcquisitionTaskDeletionService.CompleteCleanup,
                        deleteDeliveredFiles: delivery == AcquisitionTaskDeletionService.CompleteCleanup, directDirectoryDeletion: true);
                    // A registered file in a shared container can be gone while its directory
                    // and unrelated siblings are still present. Confirm those exact names only.
                    bool Gone(AcquisitionTaskDeleteCleanupGroup group) => group.Eligible &&
                        (group.ReasonCode == "AlreadyAbsent" ||
                         group is {RegisteredFilesOnly: true, ReasonCode: null, FilesTruncated: false, Files.Count: > 0} &&
                         group.FileCount == group.Files.Count &&
                         ArtifactCleanupAbsence.AllConfirmedAbsent(group.Directory, group.Files.Select(f => f.Path).ToArray(), ct));
                    if (check.Summary.ReasonCode == null && check.Summary.Groups.Count > 0 && check.Summary.Groups.All(Gone))
                    { deletedAt = DateTime.UtcNow; codes = ""; }
                    else codes = DeferredStageCleanup.RequiresReview;
                }
            }
            await db.AcquisitionTasks.Where(t => t.Id == taskId && t.DeletedAt == task.DeletedAt && t.PlanRevision == task.PlanRevision &&
                    t.UpdatedAt == task.UpdatedAt && t.WorkflowRunId == task.WorkflowRunId && t.Status == task.Status &&
                    t.DeletedDeliveryCleanup == task.DeletedDeliveryCleanup && t.DeletedIntermediateCleanup == task.DeletedIntermediateCleanup)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.DeletedIntermediateCleanup, intermediate)
                    .SetProperty(t => t.DeletedDeliveryCleanup, delivery).SetProperty(t => t.DeletedCleanupReasons, codes)
                    .SetProperty(t => t.DeletedAt, deletedAt), ct);
        }
        finally { gate.Release(); }
    }

    private async Task<DeferredEmptyDirectoryResult> FinishSmbReceiptAsync(AcquisitionTaskDbModel task, string id, AcquisitionCleanupScopes scope, CancellationToken ct)
    {
        var locks = new List<SemaphoreSlim>();
        try
        {
            var path = Path.Combine(maintenance.StageCleanupMetadataDirectory, id + ".json");
            ArtifactManifestFiles.RejectLinks(path);
            var receipt = JsonSerializer.Deserialize<AcquisitionStageDirectoryCleanupReceipt>(await File.ReadAllTextAsync(path, ct), JsonSerializerOptions.Web);
            if (receipt == null || receipt.Id != id || receipt.Revision != task.PlanRevision || receipt.TaskId != task.Id ||
                receipt.Status is not ("SmbDeletePending" or "SmbDeleteFinalized") || receipt.SelectionId == null || receipt.WaitingDirectories is not {Count: > 0} ||
                receipt.Consent is not { WholeTaskFiles: true, OwnerRunIds: not null } || receipt.Consent.TaskId != task.Id || receipt.Consent.PlanRevision != task.PlanRevision || (receipt.Scopes & scope) == 0 ||
                scope == AcquisitionCleanupScopes.Delivery && !receipt.Consent.DeleteDeliveredFiles ||
                scope == AcquisitionCleanupScopes.Intermediate && !receipt.CleanupIntermediates) return DeferredEmptyDirectoryResult.Changed;
            if (task.DeletedAt == null && (receipt.LiveTaskVersion == null || receipt.LiveTaskVersion != Version(task) ||
                receipt.Consent.CompletedRunId != task.WorkflowRunId)) return DeferredEmptyDirectoryResult.Changed;
            // While a client holds an SMB handle, only inspect the confirmed tree. The
            // run gates, global maintenance fence and reference checks are needed at mutation.
            storage.EnsurePathAllowed(receipt.Directory);
            var preflight = DeferredEmptyDirectoryTree.InspectEmptyTree(receipt.Directory, receipt.WaitingDirectories, ct);
            if (preflight != DeferredEmptyDirectoryResult.Completed) return preflight;
            var owners = receipt.Consent.OwnerRunIds;
            if (owners.Any(run => run <= 0)) return DeferredEmptyDirectoryResult.Changed;
            foreach (var run in owners.Distinct().Order())
            {
                var gate = WorkflowRunMutationGate.For(run);
                if (!await gate.WaitAsync(0, ct)) return DeferredEmptyDirectoryResult.Waiting;
                locks.Add(gate);
            }
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", JsonSerializerOptions.Web);
            if (plan == null) return DeferredEmptyDirectoryResult.Changed;
            var auth = Authorization(task, receipt.Consent, retryRetainedFiles: task.DeletedAt != null);
            var selections = await StageCleanupSelectionsAsync(task, plan, owners, receipt.CleanupIntermediates, receipt.Consent.DeleteDeliveredFiles, ct);
            var selection = selections.Selected.SingleOrDefault(s => s.Id == receipt.SelectionId && s.PartId == receipt.PartId && s.Stage == receipt.Stage);
            if (selection == null) return DeferredEmptyDirectoryResult.Changed;
            var current = await InspectStageDirectoryForDeletionAsync(task, plan, selection, selections, owners, auth,
                receipt.CleanupIntermediates, receipt.Consent.DeleteDeliveredFiles, ct, null, directDirectoryDeletion: true);
            if (current.RootDirectory != receipt.Directory || current.BindingFingerprint != receipt.BindingFingerprint) return DeferredEmptyDirectoryResult.Changed;
            if (!current.Group.Eligible) return current.Group.ReasonCode is "WorkspaceUnavailable" or "ActiveConsumer"
                ? DeferredEmptyDirectoryResult.Waiting : DeferredEmptyDirectoryResult.Changed;
            if (current.Group.ReasonCode != "AlreadyAbsent")
            {
                if (receipt.Status == "SmbDeleteFinalized") return DeferredEmptyDirectoryResult.Changed;
                var result = DeferredEmptyDirectoryTree.TryRemoveEmptyTree(receipt.Directory, receipt.WaitingDirectories, ct);
                if (result != DeferredEmptyDirectoryResult.Completed) return result;
                if (ArtifactCleanupAbsence.Probe(Path.GetDirectoryName(receipt.Directory)!, receipt.Directory, ct) != ArtifactCleanupPathPresence.ConfirmedAbsent)
                    return DeferredEmptyDirectoryResult.Waiting;
            }
            if (receipt.Status != "SmbDeleteFinalized")
                await AcquisitionStageDirectoryCleanupReceipt.SaveAsync(maintenance.StageCleanupMetadataDirectory,
                    receipt with { Status = "SmbDeleteFinalized", FinishedAt = DateTime.UtcNow }, CancellationToken.None);
            return DeferredEmptyDirectoryResult.Completed;
        }
        catch (UserStoragePathException error) when (!error.MountUnavailable) { return DeferredEmptyDirectoryResult.Changed; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or JsonException or ArgumentException) { return DeferredEmptyDirectoryResult.Changed; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return DeferredEmptyDirectoryResult.Waiting; }
        finally { foreach (var gate in locks) gate.Release(); }
    }
}
