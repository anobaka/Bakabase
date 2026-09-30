using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Images_InvalidResponseFailsOnceWithoutRecordingAnOutput(bool original)
    {
        using var handler = new ImageFailureHandler(original, 1, (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent("<html>synthetic unavailable image</html>")}));
        using var producer = await BuildImageFailureProducer(handler, original);
        var fileCallbacks = 0;
        var checkpoints = 0;
        producer.OnFileDownloaded += (_, _) => { Interlocked.Increment(ref fileCallbacks); return Task.CompletedTask; };

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => RunImageFailureProducer(producer,
            checkpoint: _ => { Interlocked.Increment(ref checkpoints); return Task.CompletedTask; }));

        Assert.AreEqual(1, handler.ImageAttempts, "Invalid data must not use the ten transient-network attempts.");
        Assert.AreEqual(original ? 1 : 0, handler.BeforeSendChecks);
        Assert.AreEqual(original ? 1 : 0, handler.Gallery.BalanceRequests,
            "A failed original response must not replay its financial preflight.");
        Assert.AreEqual(0, fileCallbacks);
        Assert.AreEqual(0, checkpoints);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Images_TransientResponseRetriesAndRecordsOnlyTheSuccessfulBytes(bool original)
    {
        using var handler = new ImageFailureHandler(original, 1, (_, attempt, _) =>
            Task.FromResult(attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {Content = new StringContent("busy")}
                : ImageFailureHandler.ValidImage()));
        using var producer = await BuildImageFailureProducer(handler, original);
        var fileCallbacks = 0;
        producer.OnFileDownloaded += (_, _) => { Interlocked.Increment(ref fileCallbacks); return Task.CompletedTask; };

        await RunImageFailureProducer(producer).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.AreEqual(2, handler.ImageAttempts);
        Assert.AreEqual(original ? 2 : 0, handler.BeforeSendChecks,
            "Every original retry must perform the final send-time check.");
        Assert.AreEqual(original ? 2 : 0, handler.Gallery.BalanceRequests,
            "Every original retry must reserve its potential cost again.");
        Assert.AreEqual(1, fileCallbacks);
        var result = (await _results.GetByTaskAsync(10)).Single();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        CollectionAssert.AreEqual(ImageFailureHandler.Png, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task Images_OriginalTransientRetryCannotBypassTheDurableBudget()
    {
        using var handler = new ImageFailureHandler(true, 1, (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {Content = new StringContent("busy")}));
        using var producer = await BuildImageFailureProducer(handler, true, configure: options =>
            options.OriginalImageMaximumGpCostPerTask = 1_000);

        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() =>
            RunImageFailureProducer(producer).WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.AreEqual(1, handler.ImageAttempts, "The second preflight must stop before another fullimg request.");
        Assert.AreEqual(1, handler.BeforeSendChecks);
        Assert.AreEqual(2, handler.Gallery.BalanceRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task Images_ExhaustedNodeRecoveryIsNotReplayedByEitherRetryLoop()
    {
        using var handler = new ImageFailureHandler(false, 1, (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {Content = new StringContent("busy")})) {IncludeNodeReload = true};
        using var producer = await BuildImageFailureProducer(handler, false);

        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            RunImageFailureProducer(producer).WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.AreEqual(2, handler.ImageAttempts, "The initial node and one replacement are the entire request budget.");
        Assert.AreEqual(1, handler.NodeReloadRequests);
        var retry = typeof(AbstractExHentaiDownloader).GetMethod("GetTransientRetry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(producer, [error, 1]);
        Assert.IsNull(retry, "The task-level restart must not repeat a spent node reload either.");
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task Images_ConfirmedFreeOriginalRecoversZeroFilledJpegOnceAndRecordsOneOutput()
    {
        using var handler = new ImageFailureHandler(true, 1, (_, attempt, _) =>
            Task.FromResult(attempt == 1 ? ImageFailureHandler.ZeroFilledJpeg() : ImageFailureHandler.ValidImage()))
            {IncludeNodeReload = true, RedirectOriginalToCdn = true};
        handler.Gallery.Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        using var producer = await BuildImageFailureProducer(handler, true, configure: options =>
            options.AllowOriginalImageGpSpending = false);
        var fileCallbacks = 0;
        var checkpoints = 0;
        producer.OnFileDownloaded += (_, _) => { Interlocked.Increment(ref fileCallbacks); return Task.CompletedTask; };

        await RunImageFailureProducer(producer, checkpoint: _ =>
            { Interlocked.Increment(ref checkpoints); return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.AreEqual(2, handler.ImageAttempts, "One initial fullimg entry and one permitted free recovery must be sent.");
        Assert.AreEqual(2, handler.OriginalCdnAttempts);
        Assert.AreEqual(1, handler.OriginalNodeReloadRequests);
        Assert.AreEqual(0, handler.NodeReloadRequests, "Original recovery must retain the original entry, not reload the viewing page.");
        Assert.AreEqual(2, handler.BeforeSendChecks);
        Assert.AreEqual(0, handler.Gallery.BalanceRequests);
        Assert.AreEqual(1, fileCallbacks);
        Assert.AreEqual(1, checkpoints);
        var result = (await _results.GetByTaskAsync(10)).Single();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        CollectionAssert.AreEqual(ImageFailureHandler.Png, await File.ReadAllBytesAsync(file));
        Assert.AreEqual(1, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Images_PaidOrUnknownOriginalDoesNotRecoverZeroFilledJpeg(bool unknownServerClock)
    {
        using var handler = new ImageFailureHandler(true, 1, (_, _, _) =>
            Task.FromResult(ImageFailureHandler.ZeroFilledJpeg()))
            {IncludeNodeReload = true, RedirectOriginalToCdn = true};
        if (unknownServerClock)
        {
            handler.Gallery.Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
            handler.Gallery.IncludeServerDate = false;
        }
        using var producer = await BuildImageFailureProducer(handler, true);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            RunImageFailureProducer(producer).WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.AreEqual(1, handler.ImageAttempts);
        Assert.AreEqual(1, handler.OriginalCdnAttempts);
        Assert.AreEqual(0, handler.OriginalNodeReloadRequests);
        Assert.AreEqual(0, handler.NodeReloadRequests);
        Assert.AreEqual(1, handler.BeforeSendChecks);
        Assert.AreEqual(1, handler.Gallery.BalanceRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Images_ExhaustedFreeOriginalRecoveryCannotRepeatThroughTransientRetry()
    {
        using var handler = new ImageFailureHandler(true, 1, (_, attempt, _) =>
            Task.FromResult(attempt == 1 ? ImageFailureHandler.ZeroFilledJpeg() :
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {Content = new StringContent("busy")}))
            {IncludeNodeReload = true, RedirectOriginalToCdn = true};
        handler.Gallery.Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        using var producer = await BuildImageFailureProducer(handler, true, configure: options =>
            options.AllowOriginalImageGpSpending = false);

        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
            RunImageFailureProducer(producer).WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.AreEqual(2, handler.ImageAttempts);
        Assert.AreEqual(2, handler.OriginalCdnAttempts);
        Assert.AreEqual(1, handler.OriginalNodeReloadRequests);
        Assert.AreEqual(2, handler.BeforeSendChecks);
        Assert.AreEqual(0, handler.Gallery.BalanceRequests);
        var retry = typeof(AbstractExHentaiDownloader).GetMethod("GetTransientRetry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(producer, [error, 1]);
        Assert.IsNull(retry, "A transient response after the free original recovery must not repeat the spent reload.");
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Images_FailureOrCancellationWaitsForStartedSiblingsBeforeReturning(bool callerCancellation)
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCount = 0;
        var active = 0;
        using var handler = new ImageFailureHandler(false, 3, async (image, _, token) =>
        {
            if (Interlocked.Increment(ref active) == 2) bothStarted.TrySetResult();
            try
            {
                if (image == 1 && !callerCancellation)
                {
                    await slowStarted.Task.WaitAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                        {Content = new StringContent("<html>synthetic unavailable image</html>")};
                }
                if (image == 2) slowStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    if (Interlocked.Increment(ref cancellationCount) == (callerCancellation ? 2 : 1))
                        cancelled.TrySetResult();
                    // Simulate a transport that needs asynchronous cleanup and then returns a
                    // response despite cancellation. The batch must await it and discard its bytes.
                    await releaseCleanup.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                return ImageFailureHandler.ValidImage();
            }
            finally { Interlocked.Decrement(ref active); }
        });
        using var producer = await BuildImageFailureProducer(handler, false, maxConcurrency: 2);
        var fileCallbacks = 0;
        var checkpoints = 0;
        var progress = new ConcurrentQueue<decimal>();
        producer.OnFileDownloaded += (_, _) => { Interlocked.Increment(ref fileCallbacks); return Task.CompletedTask; };
        var run = RunImageFailureProducer(producer, caller.Token,
            progress: value => { progress.Enqueue(value); return Task.CompletedTask; },
            checkpoint: _ => { Interlocked.Increment(ref checkpoints); return Task.CompletedTask; });
        try
        {
            await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (callerCancellation)
            {
                await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await caller.CancelAsync();
            }
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(run.IsCompleted, "A cancelled sibling is still cleaning up; the batch must await it.");
            releaseCleanup.TrySetResult();
            if (callerCancellation)
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            else
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreEqual(0, Volatile.Read(ref active), "No request may remain active when the batch returns.");
            Assert.AreEqual(2, handler.ImageAttempts, "The third item waiting for a concurrency slot must never start.");
            Assert.AreEqual(0, fileCallbacks);
            Assert.AreEqual(0, checkpoints);
            Assert.IsFalse(progress.Any(value => value > 0), "Late transport bytes must not publish success progress.");
            Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
            Assert.AreEqual(0, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length);
            Assert.AreEqual(0, Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Length);
        }
        finally
        {
            await caller.CancelAsync();
            releaseCleanup.TrySetResult();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* The expected failure was asserted above; always release the fixture. */ }
        }
    }

    private async Task<ExHentaiSingleWorkDownloader> BuildImageFailureProducer(ImageFailureHandler handler,
        bool original, int maxConcurrency = 1, Action<ExHentaiOptions>? configure = null)
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services => services.AddSingleton(_results));
        var options = provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value;
        options.NamingConvention = "{GalleryId}/{PageTitle}{Extension}";
        options.MaxConcurrency = maxConcurrency;
        options.PreferOriginalImages = original;
        if (original)
        {
            OriginalOptions(options);
            options.AllowOriginalImageGpSpending = true;
        }
        configure?.Invoke(options);
        var client = new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
        return new ExHentaiSingleWorkDownloader(provider, provider.GetRequiredService<IStringLocalizer<SharedResource>>(),
            client, provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
    }

    private Task RunImageFailureProducer(ExHentaiSingleWorkDownloader producer, CancellationToken ct = default,
        Func<decimal, Task>? progress = null, Func<string, Task>? checkpoint = null)
    {
        var method = typeof(AbstractExHentaiDownloader).GetMethod("DownloadSingleWork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(producer, [10, "https://exhentai.org/g/12345/abcdef0123/", null, _root,
            (Func<string, Task>)(_ => Task.CompletedTask), (Func<string, Task>)(_ => Task.CompletedTask),
            progress ?? (_ => Task.CompletedTask), checkpoint ?? (_ => Task.CompletedTask), ct,
            false, false, null, null, null, null])!;
    }

    private sealed class ImageFailureHandler : HttpMessageHandler
    {
        internal static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        internal readonly GalleryHandler Gallery;
        private readonly HttpMessageInvoker _gallery;
        private readonly bool _original;
        private readonly Func<int, int, CancellationToken, Task<HttpResponseMessage>> _respond;
        private readonly ConcurrentDictionary<int, int> _attempts = new();
        internal int ImageAttempts;
        internal int BeforeSendChecks;
        internal bool IncludeNodeReload;
        internal int NodeReloadRequests;
        internal bool RedirectOriginalToCdn;
        internal int OriginalNodeReloadRequests;
        internal int OriginalCdnAttempts;

        internal ImageFailureHandler(bool original, int imageCount,
            Func<int, int, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _original = original;
            _respond = respond;
            Gallery = new GalleryHandler
            {
                ImagesPerPage = imageCount, OriginalLinkSize = original ? "1 MB" : null,
                Posted = DateTimeOffset.UtcNow.AddDays(-400).ToUnixTimeSeconds()
            };
            _gallery = new HttpMessageInvoker(Gallery);
        }

        internal static HttpResponseMessage ValidImage() => new(HttpStatusCode.OK)
            {Content = new ByteArrayContent(Png)};

        internal static HttpResponseMessage ZeroFilledJpeg()
        {
            var content = new ByteArrayContent(new byte[1024]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) {Content = content};
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (_original && RedirectOriginalToCdn && request.RequestUri.Host == "fixture.hath.network")
            {
                Interlocked.Increment(ref OriginalCdnAttempts);
                var cdnAttempt = int.Parse(Path.GetFileNameWithoutExtension(path));
                var cdnResponse = await _respond(1, cdnAttempt, ct);
                cdnResponse.Headers.Date = DateTimeOffset.UtcNow;
                return cdnResponse;
            }
            if (!(_original ? path == "/fullimg.php" : path.StartsWith("/image/", StringComparison.Ordinal)))
            {
                var page = await _gallery.SendAsync(request, ct);
                if (IncludeNodeReload && path.StartsWith("/g/", StringComparison.Ordinal))
                {
                    var html = await page.Content.ReadAsStringAsync(ct);
                    page.Content.Dispose();
                    page.Content = new StringContent(html.Replace("href='https://exhentai.org/s/12345'",
                        "href='https://exhentai.org/s/abcdef0123/12345-1'", StringComparison.Ordinal));
                }
                else if ((IncludeNodeReload || !_original) && path.StartsWith("/s/", StringComparison.Ordinal))
                {
                    if (IncludeNodeReload && request.RequestUri.Query.Contains("nl=", StringComparison.Ordinal))
                        Interlocked.Increment(ref NodeReloadRequests);
                    var html = await page.Content.ReadAsStringAsync(ct);
                    if (!_original)
                        html = html.Replace("src='https://exhentai.org/image/",
                            "src='https://fixture.hath.network/image/", StringComparison.Ordinal);
                    if (IncludeNodeReload)
                        html += "<a id='loadfail' onclick=\"return nl('123-recoverykey')\">Reload</a>";
                    page.Content.Dispose();
                    page.Content = new StringContent(html);
                }
                return page;
            }
            if (request.Options.TryGetValue(ThirdPartyRequestOptions.BeforeSend, out var beforeSend))
            {
                await beforeSend(ct);
                Interlocked.Increment(ref BeforeSendChecks);
            }
            Interlocked.Increment(ref ImageAttempts);
            var image = _original || !path.Contains('-') ? 1 : int.Parse(path.Split('-').Last());
            var attempt = _attempts.AddOrUpdate(image, 1, (_, value) => value + 1);
            if (_original && request.RequestUri.Query.Contains("nl=", StringComparison.Ordinal))
                Interlocked.Increment(ref OriginalNodeReloadRequests);
            if (_original && RedirectOriginalToCdn)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri($"https://fixture.hath.network/original/{attempt}.jpg");
                return redirect;
            }
            var response = await _respond(image, attempt, ct);
            response.Headers.Date = DateTimeOffset.UtcNow;
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _gallery.Dispose();
            base.Dispose(disposing);
        }
    }
}
