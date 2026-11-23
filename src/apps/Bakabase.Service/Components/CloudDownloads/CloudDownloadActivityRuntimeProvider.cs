using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Facts already bound to an execution by its caller. They never authorize a download.</summary>
public sealed record CloudDownloadRuntimeEvidence(IReadOnlyList<WorkflowRuntimeTransferSnapshot> Downloads,
    bool Completed, bool Attention, bool BindingsComplete) : IWorkflowActivityRuntimeEvidence
{
    public CloudDownloadRuntimeSummary Summarize()
    {
        var done = Sum(d => d.CompletedBytes);
        var total = Sum(d => d.TotalBytes);
        var active = Downloads.Where(IsTransferring).ToArray();
        var speed = active.Length > 0 && active.All(d => d.SpeedBytesPerSecond is >= 0)
            ? active.Sum(d => d.SpeedBytesPerSecond!.Value) : (double?)null;
        return new(done, total, speed.HasValue && double.IsFinite(speed.Value) ? speed : null,
            Downloads.Any(d => d.CompletedBytes.HasValue) && done == null || Downloads.Any(d => d.TotalBytes.HasValue) && total == null,
            Downloads.Count, Downloads.Count(d => d.Status is "running" or "queued" or "stopping"),
            Downloads.Count(d => d.Status == "completed"), Completed ? 0 : Downloads.Count(d => d.Status == "failed"));
    }

    private long? Sum(Func<WorkflowRuntimeTransferSnapshot, long?> value)
    {
        long sum = 0;
        foreach (var download in Downloads)
        {
            if (value(download) is not long count || count < 0 || count > long.MaxValue - sum) return null;
            sum += count;
        }
        return Downloads.Count == 0 ? null : sum;
    }

    internal static bool IsTransferring(WorkflowRuntimeTransferSnapshot download) =>
        download.Status == "running" && download.Stage is null or "Downloading";

    internal long? RemainingSeconds()
    {
        long? slowest = null;
        foreach (var download in Downloads)
        {
            if (download.Status == "completed") continue;
            if (!IsTransferring(download) || download.SpeedBytesPerSecond is not double rate || !double.IsFinite(rate) || rate <= 0 ||
                download.TotalBytes is not long total || total <= 0 || download.CompletedBytes is not long done || done < 0 || done >= total) return null;
            var seconds = Math.Ceiling((total - done) / rate);
            if (seconds > TimeSpan.MaxValue.TotalSeconds) return null;
            slowest = Math.Max(slowest ?? 0, (long)seconds);
        }
        return slowest;
    }
}

public sealed record CloudDownloadRuntimeSummary(long? CompletedBytes, long? TotalBytes, double? SpeedBytesPerSecond,
    bool BytesPartial, int Count, int ActiveCount, int CompletedCount, int FailedCount);

public sealed class CloudDownloadActivityRuntimeProvider : IWorkflowActivityRuntimeProvider
{
    public bool CanHandle(string kind) => kind.StartsWith("download.", StringComparison.Ordinal) && kind is not ("download.processFiles" or "download.cleanupArtifacts") ||
        kind is "baidu.transfer" or "baidu.download" or "cloud.download" or "acquisition.fetchSupported" or
            "acquisition.fetchHttp" or "acquisition.fetchMagnet" or "acquisition.fetchExHentai";

    public ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Project(context));
    }

    private static WorkflowActivityRuntimeSnapshot Project(WorkflowActivityRuntimeContext context)
    {
        var evidence = context.Evidence.OfType<CloudDownloadRuntimeEvidence>().FirstOrDefault();
        var capability = new WorkflowCapabilityRuntime("download", "Download", "workflow.capability.download",
            ExecutionPolicy: "downloadCenter");
        if (evidence == null) return new() {Capability = capability};
        var downloads = evidence.Downloads;
        var summary = evidence.Summarize();
        var transferring = downloads.All(d => d.Stage is not ("Verifying" or "Preparing" or "Reusing" or "Checking"
            or DownloadFileVerification.Source or DownloadFileVerification.Local));
        double? progress = !evidence.Completed && transferring ? downloads.Count == 1 ? downloads[0].Progress :
            summary.TotalBytes > 0 ? 100d * summary.CompletedBytes / summary.TotalBytes : null : null;
        if (progress.HasValue && (!double.IsFinite(progress.Value) || progress is < 0 or > 100)) progress = null;
        var status = downloads.Any(d => d.Status == "running") ? "running" : downloads.Any(d => d.Status == "queued") ? "queued" :
            downloads.Any(d => d.Status == "failed") ? "failed" : downloads.Count > 0 && downloads.All(d => d.Status == "completed") ? "completed" : null;
        var stage = downloads.Count == 1 ? downloads[0].Stage : null;
        if (downloads.Count > 1)
        {
            var runningStages = downloads.Where(d => d.Status == "running").Select(d => d.Stage).Distinct().ToArray();
            if (runningStages.Length > 0 && runningStages.All(s => s is DownloadFileVerification.Source or DownloadFileVerification.Local))
                stage = runningStages.Length == 1 ? runningStages[0] : "Verifying";
        }
        return new()
        {
            Capability = capability with {Status = status},
            Stage = stage,
            Progress = progress, ProgressScope = progress.HasValue ? "download" : null,
            CompletedBytes = summary.CompletedBytes, TotalBytes = summary.TotalBytes,
            SpeedBytesPerSecond = summary.SpeedBytesPerSecond,
            EstimatedRemainingSeconds = !evidence.Completed && !evidence.Attention && evidence.BindingsComplete ? evidence.RemainingSeconds() : null
        };
    }
}
