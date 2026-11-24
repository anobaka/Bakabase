using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRuntimeDownload(string Id, string? ParentId, int? WorkflowRunId,
    string Status, double? Progress, long? CompletedBytes, long? TotalBytes, DateTimeOffset ObservedAt,
    string? ProblemCode, double? SpeedBytesPerSecond = null, string? Stage = null)
    : WorkflowRuntimeTransferSnapshot(Id, ParentId, WorkflowRunId, Status, Progress, CompletedBytes, TotalBytes,
        ObservedAt, ProblemCode, SpeedBytesPerSecond, Stage);

/// <summary>Read snapshots once for an entire page. No registry binding, reconcile, enqueue or retry.</summary>
public interface IAcquisitionRuntimeDownloadReader
{
    Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct);
    Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadThroughputAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct) =>
        ReadAsync(siteIds, transportIds, runIds, ct);
}

public sealed class AcquisitionRuntimeDownloadReader(BakabaseDbContext db, DownloadTaskService sites,
    IBtDownloadManager bt, IHttpDownloadManager http) : IAcquisitionRuntimeDownloadReader
{
    public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct) =>
        ReadStatesAsync(siteIds, transportIds, runIds, false, ct);

    public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadThroughputAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct) =>
        ReadStatesAsync(siteIds, transportIds, runIds, true, ct);

    private async Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadStatesAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, bool throughputOnly, CancellationToken ct)
    {
        if (throughputOnly)
        {
            var activeSites = await sites.GetActiveRuntimeIds(ct);
            activeSites.UnionWith(siteIds);
            siteIds = activeSites;
        }
        var result = new List<AcquisitionRuntimeDownload>();
        var activeParents = new HashSet<int>();
        foreach (var task in await sites.GetRuntimeDtos(siteIds, ct, includeFileSizes: !throughputOnly))
        {
            if ((int)task.Status is >= 300 and <= 500 || task.FileVerification != null) activeParents.Add(task.Id);
            if (task.FileVerification is {} verification)
            {
                result.Add(new("site:" + task.Id, null, verification.WorkflowRunId, "running", null, null, null,
                    verification.ObservedAt, null, Stage: verification.Stage));
                continue;
            }
            result.Add(new("site:" + task.Id, null, null, (int) task.Status switch
            {100 when task.NextStartDt == null || task.NextStartDt <= DateTime.Now => "queued",
                200 => "queued", 300 or 400 => "running", 500 => "stopping", 600 => "completed", 700 => "failed", _ => "stopped"},
                (double) task.Progress, task.TransferredBytes ?? task.DownloadedBytes, task.TotalBytes,
                // Enabled queue/running state is observed now; the persisted status timestamp may
                // still describe its previous failure. Completed/failed evidence keeps its real age.
                (int)task.Status is >= 100 and <= 400 ? DateTimeOffset.UtcNow :
                    task.TransferUpdatedAt is {} measured ? Utc(measured) : Local(task.DownloadStatusUpdateDt),
                task.Status == InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants.DownloadTaskStatus.Failed
                    ? AcquisitionRuntimeProblems.Code(task.Message) : AcquisitionRuntimeProblems.Code(task.Current),
                task.DownloadSpeedBytesPerSecond, SafeStage(task.Current)));
        }

        IReadOnlyList<BtDownloadJob> btJobs = [];
        IReadOnlyList<HttpDownloadJob> httpJobs = [];
        if (throughputOnly || runIds.Count > 0 || siteIds.Count > 0 || transportIds.Any(i => i.StartsWith("bt:", StringComparison.Ordinal)))
            btJobs = throughputOnly ? await bt.ReadActiveStatusAsync(activeParents, ct, NativeIds("bt:")) : await bt.ReadSnapshotAsync(ct);
        if (throughputOnly || runIds.Count > 0 || siteIds.Count > 0 || transportIds.Any(i => i.StartsWith("http:", StringComparison.Ordinal)))
            httpJobs = throughputOnly ? await http.ReadActiveStatusAsync(activeParents, ct, NativeIds("http:")) : await http.ReadSnapshotAsync(ct);
        HashSet<string> NativeIds(string prefix) => transportIds.Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(id => id[prefix.Length..]).ToHashSet();
        // Validate only candidate native children; each must belong to its site's latest result generation.
        var candidates = btJobs.Where(j => j.DownloadTaskId is {} site && (throughputOnly || siteIds.Contains(site))).Select(j => j.DownloadResultId)
            .Concat(httpJobs.Where(j => j.DownloadTaskId is {} site && (throughputOnly || siteIds.Contains(site))).Select(j => j.DownloadResultId))
            .OfType<int>().ToHashSet();
        var latestResults = candidates.Count == 0 ? [] : (await db.DownloadResults.AsNoTracking().Where(r =>
            candidates.Contains(r.Id) && !db.DownloadResults.Any(n => n.DownloadTaskId == r.DownloadTaskId &&
                n.SourceKey == r.SourceKey && n.Id > r.Id)).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        bool Included(string id, int? runId, int? parent, int? resultId) => throughputOnly
            ? parent == null || resultId.HasValue && latestResults.Contains(resultId.Value)
            : transportIds.Contains(id) || (parent.HasValue ? siteIds.Contains(parent.Value) &&
                resultId.HasValue && latestResults.Contains(resultId.Value) : runId.HasValue && runIds.Contains(runId.Value));
        string? Problem(string? error) => AcquisitionRuntimeProblems.Code(error) ??
            (throughputOnly && !string.IsNullOrEmpty(error) ? "TRANSFER_ERROR" : null);
        foreach (var task in btJobs)
            if (Included("bt:" + task.Id, task.WorkflowRunId, task.DownloadTaskId, task.DownloadResultId))
                result.Add(new("bt:" + task.Id, task.DownloadTaskId is {} p ? "site:" + p : null,
                    task.WorkflowRunId, task.Status.ToString().ToLowerInvariant(), task.Percentage,
                    task.CompletedBytes, task.TotalBytes, Utc(task.UpdatedAt), Problem(task.Error),
                    task.SpeedBytesPerSecond, SafeStage(task.Stage)));
        foreach (var task in httpJobs)
            if (Included("http:" + task.Id, task.WorkflowRunId, task.DownloadTaskId, task.DownloadResultId))
                result.Add(new("http:" + task.Id, task.DownloadTaskId is {} p ? "site:" + p : null,
                    task.WorkflowRunId, task.Status.ToString().ToLowerInvariant(), task.Percentage,
                    task.CompletedBytes, task.TotalBytes, Utc(task.UpdatedAt), Problem(task.Error),
                    task.SpeedBytesPerSecond, SafeStage(task.Stage)));
        return result;
    }
    internal static DateTimeOffset Local(DateTime value) => new(value.ToUniversalTime());
    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    internal static string? SafeStage(string? stage) => stage?.Trim().ToLowerInvariant() switch
    {
        "queued" => "Queued", "starting" => "Starting", "verifying" => "Verifying", "saving" => "Saving",
        "completed" => "Completed", "failed" => "Failed", "stopped" => "Stopped", "stopping" => "Stopping",
        "fetchingmetadata" => "FetchingMetadata", "connecting" => "Connecting", "checking" => "Checking",
        "preparing" or "discovering" => "Preparing", "reusing" => "Reusing",
        "transferpaused" => "TransferPaused", "transferretrying" => "TransferRetrying",
        "transferfinishing" => "TransferFinishing", "transferfailed" => "TransferFailed",
        "transfercancelled" => "TransferCancelled", "transfermixed" => "TransferMixed",
        "transferstateunavailable" => "TransferStateUnavailable", "waitingfortransferquota" => "WaitingForTransferQuota",
        "downloading" or "downloading original files" or "downloading original files with rclone" => "Downloading",
        _ => null
    };
}
