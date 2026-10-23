using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Services;

/// <summary>
/// On-demand, shared sampling for all browser windows. Directory walks never block HTTP requests,
/// never overlap and refresh at most once per five minutes while someone is watching.
/// </summary>
public sealed class ResourceUsageService(string dataDirectory, CancellationToken stoppingToken)
{
    private readonly object _gate = new();
    private long _lastCpuTimestamp = Stopwatch.GetTimestamp();
    private TimeSpan _lastCpuTime = ReadCpuTime();
    private double? _cpuPercent;
    private Task? _scan;
    private long? _directoryBytes;
    private DateTimeOffset? _directoryUpdatedAt;
    private DateTimeOffset _nextScan;
    private bool _partial;
    private bool _unavailable;

    public ResourceUsageViewModel GetSnapshot()
    {
        lock (_gate)
        {
            using var process = Process.GetCurrentProcess();
            var timestamp = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastCpuTimestamp, timestamp);
            if (elapsed >= TimeSpan.FromSeconds(1))
            {
                var cpuTime = process.TotalProcessorTime;
                _cpuPercent = CalculateCpuPercent(cpuTime - _lastCpuTime, elapsed, Environment.ProcessorCount);
                _lastCpuTime = cpuTime;
                _lastCpuTimestamp = timestamp;
            }

            if ((_scan == null || _scan.IsCompleted) && DateTimeOffset.UtcNow >= _nextScan &&
                !stoppingToken.IsCancellationRequested)
            {
                _scan = Task.Run(RefreshDirectorySize, CancellationToken.None);
            }

            return new ResourceUsageViewModel
            {
                CpuPercent = _cpuPercent,
                MemoryBytes = process.WorkingSet64,
                DataDirectoryBytes = _directoryBytes,
                DataDirectoryUpdatedAt = _directoryUpdatedAt,
                DataDirectoryScanning = _scan is { IsCompleted: false },
                DataDirectoryPartial = _partial,
                DataDirectoryUnavailable = _unavailable
            };
        }
    }

    private void RefreshDirectorySize()
    {
        try
        {
            var result = MeasureDirectory(dataDirectory, stoppingToken);
            lock (_gate)
            {
                _directoryBytes = result.Bytes;
                _partial = result.Partial;
                _unavailable = false;
                _directoryUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the last successful measurement, visibly stale, instead of reporting zero.
            lock (_gate) { _unavailable = true; }
        }
        finally
        {
            lock (_gate) { _nextScan = DateTimeOffset.UtcNow.AddMinutes(5); }
        }
    }

    internal static double CalculateCpuPercent(TimeSpan cpu, TimeSpan elapsed, int processorCount) =>
        elapsed <= TimeSpan.Zero || processorCount <= 0 ? 0 :
            Math.Clamp(cpu.TotalMilliseconds / elapsed.TotalMilliseconds / processorCount * 100, 0, 100);

    internal static (long Bytes, bool Partial) MeasureDirectory(string root, CancellationToken ct)
    {
        var result = AppStorageUsageScanner.Scan(root, ct);
        return (result.TotalBytes, result.Partial);
    }

    private static TimeSpan ReadCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }
}
