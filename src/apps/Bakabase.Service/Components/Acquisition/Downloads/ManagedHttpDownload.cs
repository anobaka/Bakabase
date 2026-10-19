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
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Service.Components.Downloader;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Downloads;

/// <summary>The workflow owns continuation; the manager owns durable transport and file publication.</summary>
internal static class ManagedHttpDownload
{
    public static async Task<HttpDownloadJob> DownloadAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        HttpDownloadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var manager = ctx.ServiceProvider.GetRequiredService<IHttpDownloadManager>();
        var registry = ctx.ServiceProvider.GetService<IDownloadTaskRegistry>();
        var owner = ctx.WorkflowRunId?.ToString(CultureInfo.InvariantCulture) ?? "working-" +
            Hash(Path.GetFullPath(ctx.WorkingDirectory));
        request = request with
        {
            Name = string.IsNullOrWhiteSpace(item.Title) ? item.WorkingName : item.Title,
            IdempotencyKey = $"workflow:{owner}:{Hash(request.Url)}", SourceKind = "workflow", SourceId = owner,
            WorkflowRunId = ctx.WorkflowRunId, WorkflowDefinitionId = ctx.WorkflowDefinitionId,
            DownloadTaskId = PositiveVariable(item, "downloadTaskId"),
            DownloadResultId = PositiveVariable(item, "downloadResultId")
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout);
        HttpDownloadJob? job = null;
        var waiting = false;
        try
        {
            job = await manager.EnsureQueuedAsync(request, deadline.Token);
            if (registry != null) await registry.EnsureBindingAsync(DownloadCenterBindings.For(job), deadline.Token);
            if (job.Status == HttpDownloadStatus.Stopping) job = await manager.StopAsync(job.Id, deadline.Token);
            if (job.Status is HttpDownloadStatus.Stopped or HttpDownloadStatus.Failed)
            {
                if (registry != null) await registry.SetUserStoppedAsync("http:" + job.Id, false, deadline.Token);
                job = await manager.ResumeAsync(job.Id, deadline.Token);
            }
            else if (job.Status == HttpDownloadStatus.Completed &&
                     (job.Files.Count == 0 || job.Files.Any(path => !File.Exists(path))))
            {
                if (registry != null) await registry.SetUserStoppedAsync("http:" + job.Id, false, deadline.Token);
                job = await manager.RecheckAsync(job.Id, deadline.Token);
            }
            waiting = true;
            return await manager.WaitForCompletionAsync(job.Id,
                state => ctx.ReportProgress(state.Percentage ?? 0, state.SpeedBytesPerSecond is { } speed
                    ? $"{state.Stage} · {state.CompletedBytes:N0} / {state.TotalBytes:N0} bytes · {speed:N0} B/s"
                    : state.Stage), stopOnCancellation: true, ct: deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!waiting && job != null) await manager.StopAsync(job.Id, CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"The HTTP download did not finish within {request.Timeout.TotalMinutes:0.##} minutes.");
        }
        catch
        {
            if (job != null) await manager.StopAsync(job.Id, CancellationToken.None);
            throw;
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static int? PositiveVariable(AcquisitionWorkItem item, string name) =>
        item.Variables.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
