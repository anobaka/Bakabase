using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed class AcquisitionThroughputSampler(IServiceScopeFactory scopes, IFileIoObservationProvider fileIo,
    AcquisitionThroughputHistory history, ILogger<AcquisitionThroughputSampler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clock = TimeProvider.System;
        var lastTick = clock.GetTimestamp();
        var reportedFailure = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(AcquisitionThroughputHistory.IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var tick = clock.GetTimestamp();
            var at = clock.GetUtcNow();
            var elapsed = clock.GetElapsedTime(lastTick, tick);
            lastTick = tick;
            try
            {
                var counters = fileIo.Drain();
                await using var scope = scopes.CreateAsyncScope();
                var source = scope.ServiceProvider.GetRequiredService<IAcquisitionThroughputDownloadSource>();
                var owners = await source.ReadAsync(counters, stoppingToken);
                var tasks = Measure(owners, counters, elapsed.TotalSeconds);
                history.Append(at, Total(tasks), tasks);
                reportedFailure = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (!reportedFailure) logger.LogWarning(ex, "Could not sample acquisition throughput");
                reportedFailure = true;
                history.Append(at, new(null, null, null, true, true), []);
            }
        }
    }

    internal static IReadOnlyList<AcquisitionTaskThroughput> Measure(AcquisitionThroughputOwners owners,
        IReadOnlyList<FileIoObservationSample> observations, double seconds)
    {
        var byTask = observations.Where(s => owners.FileOwners.ContainsKey((s.OwnerKind, s.OwnerId)))
            .ToLookup(s => owners.FileOwners[(s.OwnerKind, s.OwnerId)]);
        var result = new List<AcquisitionTaskThroughput>();
        foreach (var task in owners.Tasks)
        {
            // Providers with byte-level observations include their final payload even when a transfer
            // starts and finishes between ticks. The owner reader excludes their instantaneous speed.
            var speeds = new List<double?>(task.Downloads);
            if (task.ReceivedBytes is {} received)
                speeds.Add(seconds > 0 && double.IsFinite(seconds) ? received / seconds : null);
            var transfers = speeds.Where(r => r is >= 0 && double.IsFinite(r.Value)).ToArray();
            var partialDownload = transfers.Length < speeds.Count;
            double? download = transfers.Length == 0 && partialDownload ? null : transfers.Sum();
            var io = byTask[task.TaskId].ToArray();
            var read = FileRate(io, seconds, task.MayHaveFileIo, read: true);
            var write = FileRate(io, seconds, task.MayHaveFileIo, read: false);
            if (!task.MayHaveFileIo && speeds.Count == 0 && io.Length == 0) continue;
            result.Add(new(task.TaskId, task.Title, new(download, read, write, partialDownload,
                read == null || write == null || io.Any(s => s.ExternalProcessObserved))));
        }
        return result;
    }

    private static double? FileRate(FileIoObservationSample[] observations, double seconds, bool mayHaveIo, bool read)
    {
        var measured = observations.Where(o => read ? o.ReadObserved : o.WriteObserved).ToArray();
        if (measured.Length == 0) return mayHaveIo ? null : 0;
        if (!double.IsFinite(seconds) || seconds <= 0) return null;
        return measured.Sum(o => (double)(read ? o.ReadBytes : o.WrittenBytes)) / seconds;
    }

    internal static AcquisitionThroughputRates Total(IReadOnlyList<AcquisitionTaskThroughput> tasks)
    {
        double? Sum(Func<AcquisitionThroughputRates, double?> select)
        {
            if (tasks.Count == 0) return 0;
            var values = tasks.Select(t => select(t.Rates)).Where(v => v.HasValue).ToArray();
            return values.Length == 0 ? null : values.Sum();
        }
        return new(Sum(r => r.DownloadBytesPerSecond), Sum(r => r.ReadBytesPerSecond), Sum(r => r.WriteBytesPerSecond),
            tasks.Any(t => t.Rates.DownloadPartial), tasks.Any(t => t.Rates.FileIoPartial));
    }
}
