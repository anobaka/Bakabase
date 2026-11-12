using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionRuntimeDownload(string Id, string? ParentId, int? WorkflowRunId,
    string Status, double? Progress, long? CompletedBytes, long? TotalBytes, DateTimeOffset ObservedAt,
    string? ProblemCode, double? SpeedBytesPerSecond = null, string? Stage = null);

/// <summary>Read snapshots once for an entire page. No registry binding, reconcile, enqueue or retry.</summary>
public interface IAcquisitionRuntimeDownloadReader
{
    Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct);
}

public sealed class AcquisitionRuntimeDownloadReader(BakabaseDbContext db, DownloadTaskService sites,
    IBtDownloadManager bt, IHttpDownloadManager http) : IAcquisitionRuntimeDownloadReader
{
    public async Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
        IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct)
    {
        var result = new List<AcquisitionRuntimeDownload>();
        foreach (var task in await sites.GetRuntimeDtos(siteIds, ct))
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

        // Only the latest result generation of each site work may own an inferred native child.
        var latestResults = await db.DownloadResults.AsNoTracking().Where(r => siteIds.Contains(r.DownloadTaskId) &&
            !db.DownloadResults.Any(n => n.DownloadTaskId == r.DownloadTaskId && n.SourceKey == r.SourceKey && n.Id > r.Id))
            .Select(r => r.Id).ToArrayAsync(ct);
        bool Included(string id, int? runId, int? parent, int? resultId) => transportIds.Contains(id) ||
            (parent.HasValue ? siteIds.Contains(parent.Value) && resultId.HasValue && latestResults.Contains(resultId.Value) :
                runId.HasValue && runIds.Contains(runId.Value));
        if (runIds.Count > 0 || siteIds.Count > 0 || transportIds.Any(i => i.StartsWith("bt:", StringComparison.Ordinal)))
            foreach (var task in await bt.ReadSnapshotAsync(ct))
                if (Included("bt:" + task.Id, task.WorkflowRunId, task.DownloadTaskId, task.DownloadResultId))
                    result.Add(new("bt:" + task.Id, task.DownloadTaskId is {} p ? "site:" + p : null,
                        task.WorkflowRunId, task.Status.ToString().ToLowerInvariant(), task.Percentage,
                        task.CompletedBytes, task.TotalBytes, Utc(task.UpdatedAt), AcquisitionRuntimeProblems.Code(task.Error), task.SpeedBytesPerSecond, SafeStage(task.Stage)));
        if (runIds.Count > 0 || siteIds.Count > 0 || transportIds.Any(i => i.StartsWith("http:", StringComparison.Ordinal)))
            foreach (var task in await http.ReadSnapshotAsync(ct))
                if (Included("http:" + task.Id, task.WorkflowRunId, task.DownloadTaskId, task.DownloadResultId))
                    result.Add(new("http:" + task.Id, task.DownloadTaskId is {} p ? "site:" + p : null,
                        task.WorkflowRunId, task.Status.ToString().ToLowerInvariant(), task.Percentage,
                        task.CompletedBytes, task.TotalBytes, Utc(task.UpdatedAt), AcquisitionRuntimeProblems.Code(task.Error), task.SpeedBytesPerSecond, SafeStage(task.Stage)));
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
        "downloading" or "downloading original files" or "downloading original files with rclone" => "Downloading",
        _ => null
    };
}
