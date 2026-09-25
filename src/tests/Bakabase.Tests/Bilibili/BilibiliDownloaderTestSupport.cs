using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Media;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests.Bilibili;

/// <summary>ffmpeg as the tests need it: ready by default, or scripted to be installing / missing.</summary>
internal sealed class ControllableFfMpegService(
    ILoggerFactory loggerFactory,
    AppService appService,
    IHttpClientFactory httpClientFactory,
    CompressedFileService compressedFileService,
    IServiceProvider globalServiceProvider)
    : FfMpegService(loggerFactory, appService, httpClientFactory, compressedFileService, globalServiceProvider)
{
    /// <summary>Called on every readiness check; null = ready.</summary>
    public Func<ControllableFfMpegService, Task>? Check { get; set; }

    public int Checks;

    public void SetStatus(DependentComponentStatus status) => Status = status;

    public override Task EnsureReadyAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref Checks);
        return Check?.Invoke(this) ?? Task.CompletedTask;
    }

    public static DependencyNotInstalledException NotReady(string message) => new("ffmpeg", "FFmpeg", message);
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>Favorites and archive answers built from parameters.</summary>
internal static class BilibiliJson
{
    public const string UploaderName = "测试UP";

    public static object Item(long id, string title, int type = 2, int attr = 0, string? bvid = null) => new
    {
        id,
        type,
        title,
        cover = "http://i0.hdslb.com/bfs/archive/example.jpg",
        page = 1,
        duration = 120,
        upper = new {mid = 100, name = UploaderName},
        attr,
        bvid = bvid ?? BvId(id),
        ugc = new {first_cid = 0},
    };

    public static string BvId(long id) => $"BV1test{id}";

    public static string List(bool hasMore, int mediaCount, params object[] items) => JsonSerializer.Serialize(new
    {
        code = 0,
        message = "0",
        data = new
        {
            info = new {id = 100, title = "默认收藏夹", media_count = mediaCount},
            medias = items.Length == 0 ? null : items,
            has_more = hasMore,
        },
    });

    public static string Folders(int mediaCount) => JsonSerializer.Serialize(new
    {
        code = 0,
        message = "0",
        data = new
        {
            count = 1,
            list = new[] {new {id = 100, fid = 1, mid = 3546571234567890, title = "默认收藏夹", media_count = mediaCount}},
        },
    });

    public static string View(long aid, bool upowerExclusive = false, params (long Cid, string Part)[] pages) =>
        JsonSerializer.Serialize(new
        {
            code = 0,
            message = "0",
            data = new
            {
                bvid = BvId(aid),
                aid,
                videos = pages.Length,
                pic = "http://i0.hdslb.com/bfs/archive/example.jpg",
                title = $"视频{aid}",
                duration = 120,
                rights = new {is_stein_gate = 0, pay = 0, ugc_pay = 0, arc_pay = 0},
                pages = pages.Select((p, i) => new {cid = p.Cid, page = i + 1, part = p.Part, duration = 120}).ToArray(),
                is_upower_exclusive = upowerExclusive,
                is_upower_play = false,
                is_upower_preview = false,
            },
        });
}

/// <summary>
/// A response body that delivers <c>prefix</c> and then behaves as told: break off with an
/// <see cref="IOException"/>, or trickle bytes until <see cref="Release"/> (then finish), or hang until the
/// request is cancelled.
/// </summary>
internal sealed class ScriptedBody(byte[] content, int prefixLength, ScriptedBody.Mode mode, CancellationToken requestCt)
    : Stream
{
    public enum Mode
    {
        BreakOff = 1,
        TrickleUntilReleased = 2,
        HangUntilCancelled = 3,
    }

    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;

    public TaskCompletionSource PrefixDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_position < prefixLength)
        {
            var n = Math.Min(buffer.Length, prefixLength - _position);
            content.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        PrefixDelivered.TrySetResult();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCt);
        switch (mode)
        {
            case Mode.BreakOff:
                throw new IOException("The connection was reset by the fake CDN.");
            case Mode.HangUntilCancelled:
                await Task.Delay(Timeout.Infinite, linked.Token);
                return 0;
            case Mode.TrickleUntilReleased:
                if (!_released.Task.IsCompleted)
                {
                    await Task.WhenAny(_released.Task, Task.Delay(100, linked.Token));
                    linked.Token.ThrowIfCancellationRequested();
                    if (!_released.Task.IsCompleted && _position < content.Length - 1)
                    {
                        buffer.Span[0] = content[_position++];
                        return 1;
                    }
                }

                var rest = Math.Min(buffer.Length, content.Length - _position);
                content.AsMemory(_position, rest).CopyTo(buffer);
                _position += rest;
                return rest;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
