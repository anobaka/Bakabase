using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.Federation;

/// <summary>A stalled source or reader cannot occupy a proxy indefinitely; active long streams remain valid.</summary>
public static class FederationMediaStream
{
    public static async Task CopyAsync(Stream source, Stream destination, TimeSpan idleTimeout, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            idle.CancelAfter(idleTimeout);
            var count = await source.ReadAsync(buffer, idle.Token);
            if (count == 0) return;
            idle.CancelAfter(idleTimeout);
            await destination.WriteAsync(buffer.AsMemory(0, count), idle.Token);
        }
    }
}
