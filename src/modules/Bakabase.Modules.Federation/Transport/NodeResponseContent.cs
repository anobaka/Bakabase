using System.Net;

namespace Bakabase.Modules.Federation.Transport;

/// <summary>Retains request and grant cancellation across ResponseHeadersRead and all subsequent body reads.</summary>
internal sealed class NodeResponseContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly CancellationTokenSource _lifetime;

    public NodeResponseContent(HttpContent inner, CancellationTokenSource lifetime)
    {
        _inner = inner;
        _lifetime = lifetime;
        foreach (var (name, values) in inner.Headers) Headers.TryAddWithoutValidation(name, values);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _inner.Headers.ContentLength ?? 0;
        return _inner.Headers.ContentLength.HasValue;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        _inner.CopyToAsync(stream, context, _lifetime.Token);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _inner.CopyToAsync(stream, context, linked.Token);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync() =>
        new CancellationBoundStream(await _inner.ReadAsStreamAsync(_lifetime.Token), _lifetime.Token);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return new CancellationBoundStream(await _inner.ReadAsStreamAsync(linked.Token), _lifetime.Token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class CancellationBoundStream(Stream inner, CancellationToken lifetime) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            lifetime.ThrowIfCancellationRequested();
            return inner.Read(buffer, offset, count);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            return await inner.ReadAsync(buffer, linked.Token);
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            return await inner.ReadAsync(buffer, offset, count, linked.Token);
        }
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            await inner.CopyToAsync(destination, bufferSize, linked.Token);
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }
}
