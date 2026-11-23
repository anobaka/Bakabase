using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Bakabase.Service.Components.Downloader;

public sealed record DownloadCenterExistingFilesPreview(int SiteTaskId, int? WorkflowRunId = null, string? ProviderId = null,
    string? OutputDirectory = null, int? FileCount = null, long? TotalBytes = null, bool CanVerify = false,
    string? ReasonCode = null, string? PreviewToken = null, DownloadVerificationProblem? VerificationProblem = null);
public sealed record DownloadCenterExistingFilesInput(string PreviewToken);
public sealed record DownloadVerificationProblem(string ReasonCode, DateTimeOffset CheckedAt, CloudVerificationLocation Location);
public sealed record DownloadCenterVerifiedFiles(int SiteTaskId, int? DownloadResultId, DownloadVerificationProblem? VerificationProblem = null);

/// <summary>Verifies one site's original download without signalling its workflow or invoking a transport.</summary>
public sealed class DownloadCenterExistingFiles(BakabaseDbContext db, CloudDownloadRecoveryService recovery,
    DownloadTaskService downloads, IMemoryCache previews)
{
    private const string Changed = "RECOVERY_DOWNLOAD_IDENTITY_CHANGED";
    private const string Review = "RECOVERY_DOWNLOAD_REVIEW_REQUIRED";
    private sealed record Owner(int? AcquisitionId, int Revision, int RunId, WorkflowRunStatus Status, int Step);
    private sealed record Selection(Owner Owner, CloudDownloadTarget Target, string Root, string Directory, string? LastFileVerificationJson);
    private sealed record Observation(Owner Owner, string Identity, DownloadVerificationProblem Problem);
    private sealed record Approval(int SiteId, Owner Owner, string Identity)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DownloadCenterVerifiedFiles? Result { get; set; }
    }

    private static int SiteId(string id) =>
        id.StartsWith("site:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), NumberStyles.None,
            CultureInfo.InvariantCulture, out var value) && value > 0 && id == $"site:{value}" ? value : 0;

    public async Task<DownloadCenterExistingFilesPreview> PreviewAsync(string id, CancellationToken ct)
    {
        var site = SiteId(id);
        try
        {
            var selected = await ReadSelectionAsync(site, ct);
            if (selected == null) return new(site, ReasonCode: Review);
            var check = await recovery.CheckSelectedNativeAsync(selected.Owner.RunId, selected.Target, selected.Root, true, ct);
            var preview = new DownloadCenterExistingFilesPreview(site, selected.Owner.RunId, selected.Target.SelectedSource?.ProviderId,
                selected.Directory, check.Inventory?.FileCount, check.Inventory?.TotalBytes);
            if (check.VerificationIdentity == null || check.Inventory is not {Available: true})
            {
                var reason = PublicReason(check.FailureCode, check.ReasonCode);
                return preview with {ReasonCode = reason, VerificationProblem = check.Inventory?.Location is { } location
                    ? new(reason, DateTimeOffset.UtcNow, location) : null};
            }
            if (check.Inventory is not {FileCount: > 0, TotalBytes: >= 0}) return preview with {ReasonCode = Review};
            var token = Guid.NewGuid().ToString("N");
            previews.Set("download-center:verify:" + token, new Approval(site, selected.Owner, check.VerificationIdentity),
                TimeSpan.FromMinutes(15));
            return preview with {CanVerify = true, PreviewToken = token, VerificationProblem = PreviousProblem(selected, check.VerificationIdentity)};
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new(site, ReasonCode: PublicReason(error.Message, "RECOVERY_CHECK_INCOMPLETE"));
        }
    }

    private static DownloadVerificationProblem? PreviousProblem(Selection selected, string identity)
    {
        if (selected.LastFileVerificationJson == null) return null;
        try
        {
            var last = JsonSerializer.Deserialize<Observation>(selected.LastFileVerificationJson, JsonSerializerOptions.Web);
            return last?.Owner == selected.Owner && last.Identity == identity ? last.Problem : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<DownloadCenterVerifiedFiles> VerifyAsync(string id, string previewToken, CancellationToken ct)
    {
        if (!Guid.TryParseExact(previewToken, "N", out _) ||
            !previews.TryGetValue<Approval>("download-center:verify:" + previewToken, out var approval) ||
            approval == null || approval.SiteId != SiteId(id))
            throw new InvalidOperationException("RECOVERY_PREVIEW_EXPIRED");
        await approval.Gate.WaitAsync(ct);
        try
        {
            if (approval.Result?.DownloadResultId is { } recorded)
            {
                if (await db.DownloadResults.AsNoTracking().AnyAsync(r => r.Id == recorded &&
                        r.DownloadTaskId == approval.SiteId, ct)) return approval.Result;
                throw new InvalidOperationException(Changed);
            }
            var acquisitionGate = approval.Owner.AcquisitionId is { } task ? AcquisitionTaskGate.For(task) : null;
            if (acquisitionGate != null) await acquisitionGate.WaitAsync(ct);
            try
            {
                async Task<Selection> Recheck(CancellationToken token)
                {
                    var selection = await ReadSelectionAsync(approval.SiteId, token);
                    if (selection?.Owner != approval.Owner) throw new InvalidOperationException(Changed);
                    var check = await recovery.CheckSelectedNativeAsync(selection.Owner.RunId, selection.Target, selection.Root, false, token);
                    if (check.VerificationIdentity != approval.Identity) throw new InvalidOperationException(Changed);
                    return selection;
                }
                async Task Validate(CancellationToken token) => _ = await Recheck(token);
                async Task Register(Func<Task> commit)
                {
                    var gate = WorkflowRunMutationGate.For(approval.Owner.RunId);
                    await gate.WaitAsync(ct);
                    try
                    {
                        await Recheck(ct);
                        await commit();
                        var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == approval.SiteId)
                            .Select(r => r.Id).SingleAsync(CancellationToken.None);
                        approval.Result = new(approval.SiteId, result);
                    }
                    finally { gate.Release(); }
                }
                var selected = await Recheck(ct);
                if (approval.Result?.VerificationProblem != null) return approval.Result;
                try
                {
                    await recovery.VerifySelectedNativeAsync(selected.Owner.RunId, selected.Target, selected.Root, downloads,
                        ct, Register, Validate);
                }
                catch (CloudProviderException error) when (error.VerificationLocation is { } location)
                {
                    await Recheck(ct);
                    var problem = new DownloadVerificationProblem(PublicReason(error.Message, "RECOVERY_EXISTING_FILES_VERIFY_FAILED"),
                        DateTimeOffset.UtcNow, location);
                    var json = JsonSerializer.Serialize(new Observation(selected.Owner, approval.Identity, problem), JsonSerializerOptions.Web);
                    await db.DownloadTasks.Where(t => t.Id == approval.SiteId && t.CompletedAt == null &&
                            t.LastFileVerificationJson == selected.LastFileVerificationJson)
                        .ExecuteUpdateAsync(update => update.SetProperty(t => t.LastFileVerificationJson, json), ct);
                    approval.Result = new(approval.SiteId, null, problem);
                }
                catch (Exception error) when (selected.LastFileVerificationJson != null &&
                    error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // The latest check had no member diagnosis. A previous mismatch must not be shown as its outcome.
                    await db.DownloadTasks.Where(t => t.Id == approval.SiteId && t.LastFileVerificationJson == selected.LastFileVerificationJson)
                        .ExecuteUpdateAsync(update => update.SetProperty(t => t.LastFileVerificationJson, (string?)null), ct);
                    throw;
                }
                return approval.Result ?? throw new InvalidOperationException(Changed);
            }
            finally { acquisitionGate?.Release(); }
        }
        finally { approval.Gate.Release(); }
    }

    private async Task<Selection?> ReadSelectionAsync(int siteId, CancellationToken ct)
    {
        if (siteId <= 0) return null;
        var pair = await (from op in db.CloudDownloadOperations.AsNoTracking()
            join native in db.DownloadTasks.AsNoTracking() on op.DownloadTaskId equals native.Id
            where op.Kind == "download" && op.DownloadTaskId == siteId
            select new {op.WorkflowRunId, op.TargetId, native.DownloadPath, native.LastFileVerificationJson}).SingleOrDefaultAsync(ct);
        if (pair == null) return null;
        var run = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => r.Id == pair.WorkflowRunId &&
            (r.Status == WorkflowRunStatus.Failed || r.Status == WorkflowRunStatus.Waiting) && r.PendingSignalJson == null)
            .Select(r => new {r.Id, r.Status, r.CurrentStepIndex, r.CurrentItemJson, r.PlanSnapshotJson}).SingleOrDefaultAsync(ct);
        if (run?.CurrentStepIndex is not { } step || step < 0 ||
            WorkflowRunPlans.Read(run.PlanSnapshotJson)?.Nodes.ElementAtOrDefault(step)?.Kind != "download.awaitCompletion") return null;
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson);
        if (workset == null) return null;
        var target = workset.Targets.SingleOrDefault(t => t.DownloadTaskId == siteId);
        if (target == null || target.Id != pair.TargetId || workset.Targets.Count(t => t.Id == target.Id) != 1) return null;
        var owner = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == run.Id)
            .Select(t => new {t.Id, t.PlanRevision, t.DeletedAt}).SingleOrDefaultAsync(ct);
        if (owner?.DeletedAt != null) return null;
        var sourceIds = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => s.WorkflowRunId == run.Id &&
            s.Module == "acquisition" && s.EntityKind == "task").Select(s => s.EntityId).Distinct().ToArrayAsync(ct);
        if (sourceIds.Any(s => s != owner?.Id.ToString(CultureInfo.InvariantCulture))) return null;
        if (owner == null) await new AcquisitionWorkflowRunMutationGuard(db).EnsureCanMutateAsync(run.Id, ct);
        return new(new(owner?.Id, owner?.PlanRevision ?? 0, run.Id, run.Status, step), target, workset.OutputDirectory, pair.DownloadPath,
            pair.LastFileVerificationJson);
    }

    internal static string PublicReason(string? detail, string fallback) =>
        detail is {Length: > 0 and <= 100} && detail.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ? detail : fallback;
}
