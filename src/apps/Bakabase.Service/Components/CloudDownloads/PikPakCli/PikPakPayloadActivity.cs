using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

/// <summary>The official CLI writes a preallocated, ranged .part file. Its length is not transfer progress.</summary>
internal sealed class PikPakPayloadActivity(string partialPath)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private FileSnapshot? _previous;

    private sealed record FileSnapshot(string Identity, DateTime WrittenAt);

    internal bool HasWritten()
    {
        var current = Read();
        var before = _previous;
        _previous = current;
        // The first sight of a file, or a different object at the same path, only sets a baseline.
        return current != null && before != null && current.Identity == before.Identity &&
               current.WrittenAt != before.WrittenAt;
    }

    private FileSnapshot? Read()
    {
        try
        {
            var info = new FileInfo(partialPath);
            if (!info.Exists || info.LinkTarget != null) return null;
            ArtifactFileIdentity.RequireRegularFile(partialPath);
            return new(ArtifactFileIdentity.Read(partialPath, allowConcurrentWrites: true), info.LastWriteTimeUtc);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Polling races the CLI's final rename. A missing observation says nothing about activity.
            return null;
        }
    }

    internal static async Task DownloadAsync(PikPakCliAccountLease account, string fileId, string output,
        Func<Task> onWrite, CancellationToken ct)
    {
        var activity = new PikPakPayloadActivity(output + ".part");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var storageWrites = 0;
        var download = account.Run(["download", fileId, "--output", output], lifetime.Token, payload: true,
            progress: () => Interlocked.Exchange(ref storageWrites, 1));
        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (!download.IsCompleted)
            {
                var tick = timer.WaitForNextTickAsync(ct).AsTask();
                if (await Task.WhenAny(download, tick) == download || !await tick) break;
                // Network-mounted files may keep a cached timestamp throughout active writes.
                if (Interlocked.Exchange(ref storageWrites, 0) != 0 || activity.HasWritten()) await onWrite();
            }
            await download;
        }
        finally
        {
            if (!download.IsCompleted)
            {
                lifetime.Cancel();
                try { await download; }
                catch { /* Preserve the callback or cancellation failure that interrupted the observation. */ }
            }
        }
    }

    internal static Func<ValueTask> ReportReads(Func<Task> report)
    {
        var last = Stopwatch.GetTimestamp();
        return async () =>
        {
            if (Stopwatch.GetElapsedTime(last) < Interval) return;
            last = Stopwatch.GetTimestamp();
            await report();
        };
    }
}
