using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Notifications are coalesced hints. Persisted intent and periodic reconciliation are authoritative.</summary>
public sealed class AcquisitionDownloadCompletionWakeup
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true});
    public void Signal() => _channel.Writer.TryWrite(true);
    public async Task WaitAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await _channel.Reader.ReadAsync(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

public sealed class AcquisitionDownloadCompletionWorker(IServiceScopeFactory scopes,
    AcquisitionDownloadCompletionWakeup wakeup, ILogger<AcquisitionDownloadCompletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cursor = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                cursor = await scope.ServiceProvider.GetRequiredService<AcquisitionDownloadCompletionCoordinator>()
                    .ReconcileBatchAsync(cursor, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
            catch (Exception) {logger.LogWarning("Acquisition download completion reconciliation is temporarily unavailable.");}
            try {await wakeup.WaitAsync(stoppingToken);}
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
        }
    }
}

public sealed class AcquisitionDownloadExecutionLifecycle(IServiceScopeFactory scopes,
    AcquisitionDownloadCompletionWakeup wakeup) : IDownloadTaskExecutionLifecycle
{
    public Task OnCompletedAsync(IReadOnlyCollection<int> taskIds) {if (taskIds.Count > 0) wakeup.Signal(); return Task.CompletedTask;}
    // Starting a downloader is never implicit authorization to process or deliver its files.
    public Task OnStartedAsync(IReadOnlyCollection<int> taskIds) => Task.CompletedTask;
    public async Task OnStoppedAsync(IReadOnlyCollection<int> taskIds)
    {
        if (taskIds.Count == 0) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var runs = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId != null &&
            taskIds.Contains(o.DownloadTaskId.Value)).Select(o => o.WorkflowRunId).Distinct().ToArrayAsync();
        var intents = scope.ServiceProvider.GetRequiredService<AcquisitionDownloadContinuationService>();
        foreach (var runId in runs)
        {
            // Downloader stop callbacks may hold the downloader action lock. Never acquire an
            // acquisition gate here (recovery owns acquisition -> downloader -> run).
            var gate = WorkflowRunMutationGate.For(runId); await gate.WaitAsync();
            try
            {
                var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId && t.DeletedAt == null).ToArrayAsync();
                foreach (var task in tasks)
                    await intents.SaveWhileLockedAsync(task, runId, AcquisitionDownloadContinuationService.Paused, CancellationToken.None);
            }
            finally {gate.Release();}
        }
    }
}
