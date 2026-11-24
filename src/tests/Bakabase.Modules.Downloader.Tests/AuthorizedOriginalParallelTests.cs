using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class AuthorizedOriginalParallelTests
{
    private const int Segment = 8 * 1024 * 1024;
    private const string Tag = "\"fixture-v1\"";
    private const long Total = 4L * Segment + 133;
    // Captured from the .841 downloader after its second request failed. Its first 8 MiB are byte 1.
    private const string OldLedger = "Qk9SMdEP0iKTdQ9c3y4SnPzauMS3WhgyyzRT17vOFVqql1L0Ho+Kw9rsIyU/dYiLDnzZ6hJbrtBPr2J/NXJpDPjM+t27kpu9zoX9vJA6Rj6WYwol+LbvW3YJDiTNkuqPtHsvZQ==";
    private static readonly AuthorizedOriginalIdentity Identity = new("fixture", "operation", "account-generation",
        "folder", "member", "immutable", Total, "fixed");
    private string _root = null!;
    private string Member => Path.Combine(_root, "member");
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestInitialize] public void Setup() => _root = Path.Combine(Path.GetTempPath(), "BakabaseParallelHttp_" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private async Task LegacyPrefix()
    {
        Directory.CreateDirectory(Member);
        await File.WriteAllBytesAsync(Path.Combine(Member, "transfer.lock"), []);
        await File.WriteAllBytesAsync(Path.Combine(Member, "checkpoint"), Convert.FromBase64String(OldLedger));
        var bytes = new byte[Segment];
        Array.Fill(bytes, (byte)1);
        await File.WriteAllBytesAsync(Path.Combine(Member, "payload.download"), bytes);
    }

    private static ServiceProvider Services(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback,
        int width = 8, bool addSecondClient = false)
    {
        var services = new ServiceCollection().AddDownloader(_ => "unused");
        Add("fixture", width);
        if (addSecondClient) Add("second", 2);
        return services.BuildServiceProvider();
        void Add(string name, int lanes) => services.AddHttpClient(name)
            .ForAuthorizedOriginalDownloads(lanes, minimumParallelBytes: 2 * Segment)
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(callback));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            callback(request, ct);
    }

    private static (long Start, long End) Range(HttpRequestMessage req)
    {
        var range = req.Headers.Range!.Ranges.Single();
        // Every full segment (and the final 0-0 recheck) is conditional on the original representation.
        if (range.To > 0 || req.Headers.IfRange != null)
        {
            Assert.AreEqual(Tag, req.Headers.IfMatch.Single().Tag);
            Assert.AreEqual(Tag, req.Headers.IfRange!.EntityTag!.Tag);
        }
        return (range.From!.Value, range.To!.Value);
    }

    private static HttpResponseMessage Response(long start, long end, long total = Total, string tag = Tag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            { Content = new StreamContent(new Pattern(start, end - start + 1)) };
        response.Headers.ETag = EntityTagHeaderValue.Parse(tag);
        response.Content.Headers.ContentLength = end - start + 1;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, total);
        return response;
    }

    // No in-memory fixture scales with the declared origin length.
    private sealed class Pattern(long start, long size) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => size;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, size - _read);
            buffer[..n].Fill((byte)(1 + start / Segment));
            _read += n;
            return n;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(Read(buffer.Span)); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private Task<string> Download(IAuthorizedOriginalHttpDownloader downloader, CancellationToken ct = default) =>
        downloader.DownloadAsync(Identity, Member, "fixture", _ => Task.FromResult(new Uri("https://cdn.example.org/original")), null, ct);

    [TestMethod]
    [DataRow("success")]
    [DataRow("failure")]
    [DataRow("changed")]
    [DataRow("laterFailure")]
    [DataRow("laterChanged")]
    [DataRow("canceled")]
    public async Task Old_checkpoint_and_out_of_order_ranges_preserve_only_the_contiguous_verified_prefix(string mode)
    {
        await LegacyPrefix();
        var lastRange = Signal();
        var holdFirst = Signal();
        var starts = new ConcurrentQueue<long>();
        using var services = Services(async (req, ct) =>
        {
            var (start, end) = Range(req);
            if (end == 0) return Response(start, end);
            starts.Enqueue(start);
            if (start == Segment)
            {
                await holdFirst.Task.WaitAsync(ct);
                if (mode == "failure") return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                if (mode == "changed") return Response(start, end, tag: "\"replaced\"");
            }
            if (start == 2L * Segment && mode == "laterFailure") return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (start == 2L * Segment && mode == "laterChanged") return Response(start, end, tag: "\"replaced\"");
            if (start == 4L * Segment) lastRange.TrySetResult();
            return Response(start, end);
        });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var running = Download(downloader, stop.Token);
        try
        {
            await lastRange.Task.WaitAsync(stop.Token);
            CollectionAssert.AreEqual(Convert.FromBase64String(OldLedger), await File.ReadAllBytesAsync(Path.Combine(Member, "checkpoint")));
            Assert.AreEqual(Segment, new FileInfo(Path.Combine(Member, "payload.download")).Length);
            Assert.IsFalse(starts.Contains(0L));
            if (mode == "canceled") await stop.CancelAsync();
        }
        finally { holdFirst.TrySetResult(); }

        if (mode == "success")
        {
            var path = await running;
            await AssertExpected(path);
            CollectionAssert.AreEqual(new long[]{Segment, 2L * Segment, 3L * Segment, 4L * Segment}, starts.Order().ToArray());
        }
        else
        {
            if (mode == "canceled") await Assert.ThrowsAsync<OperationCanceledException>(async () => await running);
            else
            {
                var error = await Assert.ThrowsExactlyAsync<AuthorizedOriginalDownloadException>(async () => await running);
                Assert.AreEqual(mode is "changed" or "laterChanged" ? "ORIGINAL_HTTP_SOURCE_CHANGED" : "ORIGINAL_HTTP_TRANSFER_FAILED", error.Code);
            }
            Assert.IsFalse(File.Exists(Path.Combine(Member, "payload")));
            var expectedPrefix = mode is "laterChanged" or "laterFailure" ? 2L * Segment : Segment;
            Assert.AreEqual(expectedPrefix, (await downloader.InspectAsync(Identity, Member, default)).VerifiedBytes);
        }
    }

    private static async Task AssertExpected(string path)
    {
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var repeated = new byte[65536];
        for (long position = 0; position < Total; position += repeated.Length)
        {
            Array.Fill(repeated, (byte)(1 + position / Segment));
            expected.AppendData(repeated, 0, (int)Math.Min(Total - position, repeated.Length));
        }
        await using var file = File.OpenRead(path);
        CollectionAssert.AreEqual(expected.GetHashAndReset(), await SHA256.HashDataAsync(file));
    }

    [TestMethod]
    [DataRow(503)]
    [DataRow(429)]
    public async Task Limited_window_yields_and_retries_only_the_remaining_prefix_on_one_lane(int status)
    {
        await LegacyPrefix();
        var requests = new ConcurrentQueue<(long Start, long Tick)>();
        var headCount = 0;
        var secondAttempt = Signal();
        var release = Signal();
        using var services = Services(async (req, ct) =>
        {
            var (start, end) = Range(req);
            if (end == 0) return Response(start, end);
            requests.Enqueue((start, Stopwatch.GetTimestamp()));
            if (start == Segment)
            {
                if (Interlocked.Increment(ref headCount) == 1)
                {
                    var limited = new HttpResponseMessage((HttpStatusCode)status);
                    limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                    return limited;
                }
                secondAttempt.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            return Response(start, end);
        });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var running = Download(services.GetRequiredService<IAuthorizedOriginalHttpDownloader>(), stop.Token);
        try
        {
            await secondAttempt.Task.WaitAsync(stop.Token);
            await Task.Delay(50, stop.Token); // A parallel fallback would also dispatch the next range while this one is held.
            var trace = requests.ToArray();
            var first = trace.First(r => r.Start == Segment);
            var retry = trace.Last(r => r.Start == Segment);
            Assert.IsTrue(Stopwatch.GetElapsedTime(first.Tick, retry.Tick) >= TimeSpan.FromMilliseconds(1900));
            Assert.AreEqual(Segment, trace.Last().Start);
            Assert.AreEqual(1, trace.Count(r => r.Tick >= retry.Tick));
        }
        finally { release.TrySetResult(); }
        await AssertExpected(await running);
        Assert.IsFalse(requests.Any(r => r.Start == 0));
    }

    [TestMethod]
    [DataRow(1, Total)]
    [DataRow(8, 2L * Segment - 1)]
    public async Task Unopted_client_or_small_file_uses_one_lane(int width, long total)
    {
        var head = Signal();
        var unblock = Signal();
        var starts = new ConcurrentQueue<long>();
        using var services = Services(async (req, ct) =>
        {
            var (start, end) = Range(req);
            if (end > 0)
            {
                starts.Enqueue(start);
                if (start == 0) { head.TrySetResult(); await unblock.Task.WaitAsync(ct); }
            }
            return Response(start, end, total);
        }, width);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var run = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>().DownloadAsync(
            Identity with {Length = total}, Member, "fixture", _ => Task.FromResult(new Uri("https://cdn.example.org/original")), null, stop.Token);
        try
        {
            await head.Task.WaitAsync(stop.Token);
            await Task.Delay(50, stop.Token);
            CollectionAssert.AreEqual(new long[] {0}, starts.ToArray());
        }
        finally { unblock.TrySetResult(); }
        Assert.AreEqual(total, new FileInfo(await run).Length);
    }

    [TestMethod]
    public async Task Lengthy_retry_after_is_reported_without_issuing_an_early_retry()
    {
        var calls = 0;
        using var services = Services((_, _) =>
        {
            calls++;
            var result = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            result.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(result);
        });
        var error = await Assert.ThrowsExactlyAsync<AuthorizedOriginalDownloadException>(() =>
            Download(services.GetRequiredService<IAuthorizedOriginalHttpDownloader>()));
        Assert.AreEqual("ORIGINAL_HTTP_RATE_LIMITED", error.Code);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task Active_range_and_buffer_window_remain_bounded_across_huge_files_clients_and_jobs()
    {
        const long enormous = 3L * 1024 * 1024 * 1024 * 1024;
        var started = 0;
        var smallerClient = 0;
        var acquired = Signal();
        var probeCount = 0;
        var probes = Signal();
        using var services = Services(async (req, ct) =>
        {
            var (start, end) = Range(req);
            if (end == 0)
            {
                if (Interlocked.Increment(ref probeCount) == 3) probes.TrySetResult();
                await probes.Task.WaitAsync(ct);
                return Response(start, end, enormous);
            }
            if (req.RequestUri!.AbsolutePath.Contains("small")) Interlocked.Increment(ref smallerClient);
            if (Interlocked.Increment(ref started) == 8) acquired.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new AssertFailedException();
        }, addSecondClient: true);
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> Job(string name, string client) => downloader.DownloadAsync(Identity with {Length = enormous, MemberId = name},
            Path.Combine(_root, name), client, _ => Task.FromResult(new Uri("https://cdn.example.org/" + name)), null, stop.Token);
        var jobs = new[] { Job("small1", "second"), Job("small2", "second"), Job("big", "fixture") };
        try
        {
            await acquired.Task.WaitAsync(stop.Token);
            Assert.AreEqual(8, Volatile.Read(ref started));
            Assert.AreEqual(2, Volatile.Read(ref smallerClient));
        }
        finally { await stop.CancelAsync(); }
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.WhenAll(jobs));
        Assert.IsTrue(jobs.All(job => job.IsCanceled));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Inspecting_a_large_handoff_does_not_allocate_its_entire_digest_log()
    {
        var identity = Identity with {Length = 4L * 1024 * 1024 * 1024 * 1024};
        Directory.CreateDirectory(Member);
        await File.WriteAllBytesAsync(Path.Combine(Member, "transfer.lock"), []);
        var header = Convert.FromBase64String(OldLedger)[..68];
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)).CopyTo(header, 36);
        using (var receipt = File.Create(Path.Combine(Member, "checkpoint")))
        {
            receipt.Write(header);
            receipt.Position = header.Length + identity.Length / Segment * 32;
            receipt.WriteByte(0xa5); // Post-handoff inspection checks the format/seal; the provider owns final-file proof.
        }
        using var services = Services((_, _) => throw new AssertFailedException("Inspection is local."));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = await downloader.InspectHandedOffAsync(identity, Member, default);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(identity.Length, result.VerifiedBytes);
        Assert.IsTrue(allocated < 8 * 1024 * 1024, $"The 16 MiB digest journal allocated {allocated} bytes during inspection.");
    }

    [TestMethod]
    public async Task Concurrent_expiry_refreshes_the_official_location_once_per_generation()
    {
        var resolved = 0;
        var expired = 0;
        var allRejected = Signal();
        using var state = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var services = Services(async (req, ct) =>
        {
            var (start, end) = Range(req);
            if (end != 0 && req.RequestUri!.Query == "?generation=1")
            {
                if (Interlocked.Increment(ref expired) == 4) allRejected.TrySetResult();
                await allRejected.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            return Response(start, end);
        });
        await LegacyPrefix();
        var path = await services.GetRequiredService<IAuthorizedOriginalHttpDownloader>().DownloadAsync(Identity,
            Member, "fixture", _ => Task.FromResult(new Uri("https://cdn.example.org/original?generation=" +
                Interlocked.Increment(ref resolved))), null, state.Token);
        Assert.AreEqual(4, expired);
        Assert.AreEqual(3, resolved, "Initial probe, one shared renewal, final independent proof.");
        await AssertExpected(path);
    }
}
