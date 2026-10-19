using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Service.Components.Downloader;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Downloads;

/// <summary>A workflow owns its wait and cancellation; the BT manager owns the durable transfer.</summary>
internal static class ManagedTorrentDownload
{
    public static async Task<TorrentDownloadResult> DownloadAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, TimeSpan timeout, CancellationToken ct,
        string? magnetUri = null, byte[]? metadata = null, string? torrentUrl = null)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "The download timeout must be between zero and 30 days.");
        ct.ThrowIfCancellationRequested();
        var manager = ctx.ServiceProvider.GetRequiredService<IBtDownloadManager>();
        var sourceHash = Convert.ToHexString(SHA256.HashData(metadata ??
            Encoding.UTF8.GetBytes(magnetUri != null ? "magnet:" + magnetUri : "url:" + torrentUrl)));
        // Non-workflow hosts have no run ID. Their stable working directory supplies the same
        // retry identity without creating a fake workflow association.
        var owner = ctx.WorkflowRunId?.ToString(CultureInfo.InvariantCulture) ?? "working-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(ctx.WorkingDirectory))));
        var request = new BtDownloadRequest
        {
            Name = string.IsNullOrWhiteSpace(item.Title) ? item.WorkingName : item.Title,
            MagnetUri = magnetUri, Metadata = metadata, TorrentUrl = torrentUrl,
            OutputDirectory = Path.Combine(Path.GetFullPath(ctx.WorkingDirectory), "torrent-data"),
            Timeout = timeout, IdempotencyKey = $"workflow:{owner}:{sourceHash}",
            SourceKind = "workflow", SourceId = owner,
            WorkflowRunId = ctx.WorkflowRunId, WorkflowDefinitionId = ctx.WorkflowDefinitionId,
            DownloadResultId = PositiveVariable(item, "downloadResultId"),
            DownloadTaskId = PositiveVariable(item, "downloadTaskId")
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        BtDownloadJob? job = null;
        var waiting = false;
        try
        {
            job = await manager.EnsureQueuedAsync(request, deadline.Token);
            var registry = ctx.ServiceProvider.GetService<IDownloadTaskRegistry>();
            if (registry != null)
                await registry.EnsureBindingAsync(DownloadCenterBindings.For(job), deadline.Token);
            if (job.Status == BtDownloadStatus.Stopping)
                job = await manager.StopAsync(job.Id, deadline.Token);
            if (job.Status is BtDownloadStatus.Stopped or BtDownloadStatus.Failed)
            {
                if (registry != null) await registry.SetUserStoppedAsync("bt:" + job.Id, false, deadline.Token);
                job = await manager.ResumeAsync(job.Id, deadline.Token);
            }
            else if (job.Status == BtDownloadStatus.Completed &&
                     (job.Files.Count == 0 || job.Files.Any(path => !File.Exists(path))))
            {
                if (registry != null) await registry.SetUserStoppedAsync("bt:" + job.Id, false, deadline.Token);
                job = await manager.RecheckAsync(job.Id, deadline.Token);
            }

            waiting = true;
            var completed = await manager.WaitForCompletionAsync(job.Id,
                state => ctx.ReportProgress(state.Percentage, state.SpeedBytesPerSecond is { } speed
                    ? $"{state.Stage} · {state.CompletedBytes:N0} / {state.TotalBytes:N0} bytes · {speed:N0} B/s"
                    : state.Stage), stopOnCancellation: true, ct: deadline.Token);
            return new TorrentDownloadResult(completed.OutputDirectory, completed.Files);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // WaitForCompletionAsync handles cancellation once entered. Cover the preceding
            // ensure/resume/recheck window as well so an abandoned workflow never leaves work queued.
            if (!waiting && job != null) await manager.StopAsync(job.Id, CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"The BT download did not finish within {timeout.TotalMinutes:0.##} minutes.");
        }
        catch
        {
            // A failed progress callback or other caller-side failure also ends ownership of
            // the wait. The capability may still be downloading, so drain it before failing.
            if (job != null) await manager.StopAsync(job.Id, CancellationToken.None);
            throw;
        }
    }

    private static int? PositiveVariable(AcquisitionWorkItem item, string name) =>
        item.Variables.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
