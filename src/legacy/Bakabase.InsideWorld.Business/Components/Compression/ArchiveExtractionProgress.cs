using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

internal static class ArchiveExtractionProgress
{
    // Native and compatibility readers expose synchronous output callbacks. Serialize
    // their UI notifications and join them before returning, rather than async-void
    // callbacks which can update an already completed/cancelled extraction task.
    internal static async Task<T> RunAsync<T>(Func<Action<string>, CancellationToken, Task<T>> extract,
        Func<int, Task> update, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var queue = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
        });
        var consuming = Consume();
        try
        {
            return await extract(line =>
            {
                stop.Token.ThrowIfCancellationRequested();
                if (SevenZipProgressOutput.TryPercentage(line, out var percentage)) queue.Writer.TryWrite(percentage);
            }, stop.Token).ConfigureAwait(false);
        }
        finally
        {
            queue.Writer.TryComplete();
            await consuming.ConfigureAwait(false);
        }

        async Task Consume()
        {
            var last = -1;
            try
            {
                await foreach (var percentage in queue.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (percentage == last) continue;
                    await update(percentage).ConfigureAwait(false);
                    last = percentage;
                }
            }
            catch
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
