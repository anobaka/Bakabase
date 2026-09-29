using System.Net;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http;

/// <summary>
/// Measures bytes consumed from a response body, including chunked responses without Content-Length.
/// Keeping the original content inside this wrapper also preserves its headers and disposal lifetime.
/// </summary>
internal sealed class ResponseTrafficTrackingContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action<long, bool> _onRead;
    private readonly object _rangeGate = new();
    private readonly List<(long Start, long End)> _readRanges = [];

    public ResponseTrafficTrackingContent(HttpContent inner, Action<long, bool> onRead)
    {
        _inner = inner;
        _onRead = onRead;

        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
        new ReadTrackingStream(_inner.ReadAsStream(cancellationToken), RecordRead);

    protected override async Task<Stream> CreateContentReadStreamAsync() =>
        new ReadTrackingStream(await _inner.ReadAsStreamAsync(), RecordRead);

    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        new ReadTrackingStream(await _inner.ReadAsStreamAsync(cancellationToken), RecordRead);

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        using var source = CreateContentReadStream(cancellationToken);
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = source.Read(buffer);
            if (bytes == 0) break;
            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(buffer, 0, bytes);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        CopyToAsync(stream, CancellationToken.None);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken) => CopyToAsync(stream, cancellationToken);

    private async Task CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        using var source = await CreateContentReadStreamAsync(cancellationToken);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private void RecordRead(long? start, long bytes, bool finished)
    {
        if (bytes == 0)
        {
            if (finished) _onRead(0, true);
            return;
        }

        // Seekable in-memory content can be replayed. Count each body byte once even when
        // a caller seeks backwards or serializes the same content again.
        if (start is { } position)
        {
            lock (_rangeGate)
            {
                var end = position + bytes;
                var newBytes = bytes;
                var index = 0;
                while (index < _readRanges.Count && _readRanges[index].End < position) index++;

                var mergedStart = position;
                var mergedEnd = end;
                while (index < _readRanges.Count && _readRanges[index].Start <= mergedEnd)
                {
                    var range = _readRanges[index];
                    newBytes -= Math.Max(0, Math.Min(end, range.End) - Math.Max(position, range.Start));
                    mergedStart = Math.Min(mergedStart, range.Start);
                    mergedEnd = Math.Max(mergedEnd, range.End);
                    _readRanges.RemoveAt(index);
                }

                _readRanges.Insert(index, (mergedStart, mergedEnd));
                bytes = newBytes;
            }
        }

        if (bytes > 0) _onRead(bytes, false);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_inner.Headers.ContentLength is { } contentLength)
        {
            length = contentLength;
            return true;
        }

        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _onRead(0, true);
        }

        base.Dispose(disposing);
    }

    private sealed class ReadTrackingStream(Stream inner, Action<long?, long, bool> onRead) : Stream
    {
        private int _finished;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var start = StartPosition();
            return Record(inner.Read(buffer, offset, count), count, start);
        }
        public override int Read(Span<byte> buffer)
        {
            var start = StartPosition();
            return Record(inner.Read(buffer), buffer.Length, start);
        }
        public override int ReadByte()
        {
            var start = StartPosition();
            var value = inner.ReadByte();
            Record(value < 0 ? 0 : 1, 1, start);
            return value;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            var start = StartPosition();
            return Record(await inner.ReadAsync(buffer, offset, count, cancellationToken), count, start);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var start = StartPosition();
            return Record(await inner.ReadAsync(buffer, cancellationToken), buffer.Length, start);
        }

        private long? StartPosition() => inner.CanSeek ? inner.Position : null;

        private int Record(int bytes, int requested, long? start)
        {
            if (bytes > 0)
            {
                Interlocked.Exchange(ref _finished, 0);
                onRead(start, bytes, false);
            }
            else if (requested > 0)
            {
                Finish();
            }

            return bytes;
        }

        private void Finish()
        {
            if (Interlocked.Exchange(ref _finished, 1) == 0)
            {
                onRead(null, 0, true);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => inner.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Finish();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Finish();
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
