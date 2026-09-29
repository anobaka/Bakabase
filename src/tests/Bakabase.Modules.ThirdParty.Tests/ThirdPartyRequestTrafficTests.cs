using System.Net;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;
using Bakabase.Modules.ThirdParty.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public class ThirdPartyRequestTrafficTests
{
    [TestMethod]
    public async Task ChunkedResponse_CountsBytesAsRead_AndBroadcastsWithoutCountingAnotherRequest()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        var notifications = new StatisticsCollector();
        using var service = new ThirdPartyService(logger, notifications, NullLogger<ThirdPartyService>.Instance);
        var requestCompletions = 0;
        var trafficChanges = 0;
        logger.OnRequestCompleted += (_, _) => requestCompletions++;
        logger.OnTrafficChanged += (_, _) => trafficChanges++;

        using var response = await logger.CaptureAsync(ThirdPartyId.ExHentai, () =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableReadStream("123456789"u8.ToArray()))
            }));

        Assert.IsNull(response.Content.Headers.ContentLength);
        Assert.AreEqual(1, requestCompletions);
        Assert.AreEqual(0L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(1, SingleSource(service).Counts![(int)ThirdPartyRequestResultType.Succeed]);

        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[4];
        Assert.AreEqual(4, await stream.ReadAsync(buffer));
        Assert.AreEqual(4L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(0, await stream.ReadAsync(Memory<byte>.Empty));
        Assert.AreEqual(4L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(4, await stream.ReadAsync(buffer));
        Assert.AreEqual(8L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(1, await stream.ReadAsync(buffer));
        Assert.AreEqual(9L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(0, await stream.ReadAsync(buffer));

        Assert.AreEqual(1, requestCompletions);
        Assert.IsTrue(trafficChanges >= 1);
        Assert.AreEqual(9L, notifications.Latest!.Single().ReceivedBytes);
        Assert.AreEqual(1, SingleSource(service).Counts![(int)ThirdPartyRequestResultType.Succeed]);

        using var otherResponse = await logger.CaptureAsync(ThirdPartyId.Pixiv, () =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableReadStream("abc"u8.ToArray()))
            }));
        CollectionAssert.AreEqual("abc"u8.ToArray(), await otherResponse.Content.ReadAsByteArrayAsync());
        var bySource = service.GetAllThirdPartyRequestStatistics().ToDictionary(x => x.Id);
        Assert.AreEqual(9L, bySource[ThirdPartyId.ExHentai].ReceivedBytes);
        Assert.AreEqual(3L, bySource[ThirdPartyId.Pixiv].ReceivedBytes);
        Assert.AreEqual(2, requestCompletions);
    }

    [TestMethod]
    public void PartialResponse_DisposePublishesOnlyBytesActuallyRead()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        var notifications = new StatisticsCollector();
        using var service = new ThirdPartyService(logger, notifications, NullLogger<ThirdPartyService>.Instance);

        using (var response = logger.Capture(ThirdPartyId.Pixiv, () =>
                   new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StreamContent(new NonSeekableReadStream("123456789"u8.ToArray()))
                   }))
        {
            var stream = response.Content.ReadAsStream();
            Assert.AreEqual(3, stream.Read(new byte[3]));
            Assert.AreEqual(3L, SingleSource(service).ReceivedBytes);
        }

        Assert.AreEqual(3L, notifications.Latest!.Single().ReceivedBytes);
        Assert.AreEqual(1, SingleSource(service).Counts![(int)ThirdPartyRequestResultType.Succeed]);
    }

    [TestMethod]
    public async Task SeekableResponse_ReplayedBodyDoesNotCountTwice()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        using var service = new ThirdPartyService(logger, null, NullLogger<ThirdPartyService>.Instance);
        using var response = await logger.CaptureAsync(ThirdPartyId.Bilibili, () =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("response"u8.ToArray())
            }));

        await using var body = await response.Content.ReadAsStreamAsync();
        Assert.IsTrue(body.CanSeek);
        await body.CopyToAsync(Stream.Null);
        Assert.AreEqual(8L, SingleSource(service).ReceivedBytes);

        body.Seek(0, SeekOrigin.Begin);
        await body.CopyToAsync(Stream.Null);
        Assert.AreEqual(8L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual(1, SingleSource(service).Counts![(int)ThirdPartyRequestResultType.Succeed]);
    }

    [TestMethod]
    public async Task BufferedReaders_CountResponseBodyOnlyOnce()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        using var service = new ThirdPartyService(logger, null, NullLogger<ThirdPartyService>.Instance);
        using var response = await logger.CaptureAsync(ThirdPartyId.Bangumi, () =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("hello"u8.ToArray())
            }));

        CollectionAssert.AreEqual("hello"u8.ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(5L, SingleSource(service).ReceivedBytes);
        Assert.AreEqual("hello", await response.Content.ReadAsStringAsync());
        Assert.AreEqual(5L, SingleSource(service).ReceivedBytes);
    }

    [TestMethod]
    public void SyncCopyHonorsCancellationBetweenChunks()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        using var service = new ThirdPartyService(logger, null, NullLogger<ThirdPartyService>.Instance);
        using var response = logger.Capture(ThirdPartyId.Pixiv, () =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[40 * 1024]) });
        using var cts = new CancellationTokenSource();
        using var destination = new CancelAfterFirstWriteStream(cts);

        Assert.ThrowsException<OperationCanceledException>(() =>
            response.Content.CopyTo(destination, null, cts.Token));
        Assert.AreEqual(16 * 1024L, SingleSource(service).ReceivedBytes);
    }

    [TestMethod]
    public async Task SlowBroadcast_CoalescesEventsIntoOneCurrentSnapshot()
    {
        var logger = new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance);
        var notifications = new BlockingStatisticsCollector();
        using var service = new ThirdPartyService(logger, notifications, NullLogger<ThirdPartyService>.Instance);

        using var firstResponse = logger.Capture(ThirdPartyId.Pixiv,
            () => new HttpResponseMessage(HttpStatusCode.OK));
        await notifications.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(5));

        for (var i = 0; i < 40; i++)
        {
            using var response = logger.Capture(ThirdPartyId.Pixiv,
                () => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[2])
                });
            await response.Content.ReadAsByteArrayAsync();
        }

        Assert.AreEqual(1, notifications.CallCount);
        notifications.ReleaseFirstCall();
        var latest = (await notifications.SecondSnapshot.WaitAsync(TimeSpan.FromSeconds(5))).Single();
        Assert.AreEqual(2, notifications.CallCount);
        Assert.AreEqual(41, latest.Counts![(int)ThirdPartyRequestResultType.Succeed]);
        Assert.AreEqual(80L, latest.ReceivedBytes);

        service.Dispose();
        using var afterDispose = logger.Capture(ThirdPartyId.Pixiv,
            () => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.AreEqual(2, notifications.CallCount);
    }

    private static ThirdPartyRequestStatistics SingleSource(ThirdPartyService service) =>
        service.GetAllThirdPartyRequestStatistics().Single();

    private sealed class StatisticsCollector : IThirdPartyStatisticsNotificationService
    {
        public ThirdPartyRequestStatistics[]? Latest { get; private set; }

        public Task NotifyStatisticsChanged(ThirdPartyRequestStatistics[] statistics)
        {
            Latest = statistics;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingStatisticsCollector : IThirdPartyStatisticsNotificationService
    {
        private readonly TaskCompletionSource<bool> _firstCallStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstCall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ThirdPartyRequestStatistics[]> _secondSnapshot =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public Task FirstCallStarted => _firstCallStarted.Task;
        public Task<ThirdPartyRequestStatistics[]> SecondSnapshot => _secondSnapshot.Task;

        public Task NotifyStatisticsChanged(ThirdPartyRequestStatistics[] statistics)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _firstCallStarted.TrySetResult(true);
                return _releaseFirstCall.Task;
            }

            _secondSnapshot.TrySetResult(statistics);
            return Task.CompletedTask;
        }

        public void ReleaseFirstCall() => _releaseFirstCall.TrySetResult(true);
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CancelAfterFirstWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }
    }
}
