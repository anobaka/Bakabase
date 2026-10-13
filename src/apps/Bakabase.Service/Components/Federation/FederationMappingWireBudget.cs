using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Media;

namespace Bakabase.Service.Components.Federation;

/// <summary>Counts serialization without retaining a second complete response buffer.</summary>
internal static class FederationMappingWireBudget
{
    // Resolve uses FederationMediaService.ReadJsonAsync's 8 MiB media envelope, not the smaller
    // generic control reader. A little headroom avoids making an otherwise valid base regress.
    public const int MaximumWireBytes = 8 * 1024 * 1024 - 4096;

    public static bool Fits(ResourceResolveResponse response, CancellationToken ct)
    {
        using var stream = new CountingStream(MaximumWireBytes, ct);
        try { JsonSerializer.Serialize(stream, response, FederationJson.Options); return true; }
        catch (BudgetExceeded) { return false; }
    }

    private sealed class BudgetExceeded : IOException { }
    private sealed class CountingStream(long limit, CancellationToken ct) : Stream
    {
        private long _bytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes;
        public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Charge(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Charge(buffer.Length);
        private void Charge(int count)
        {
            ct.ThrowIfCancellationRequested();
            if (count > limit - _bytes) throw new BudgetExceeded();
            _bytes += count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
