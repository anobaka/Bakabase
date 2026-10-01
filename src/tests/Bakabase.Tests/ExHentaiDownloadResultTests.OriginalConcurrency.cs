using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(false, 3)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    [DataRow(true, 3)]
    public async Task OriginalImages_RespectConfiguredConcurrencyDuringBodyDownloads(bool paid, int concurrency)
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var atCapacity = NewOriginalSignal();
        var releaseBodies = NewOriginalSignal();
        var imageCount = concurrency * 2 + 1;
        using var handler = new ConcurrentOriginalHandler(imageCount, paid);
        handler.BeforeBodyRead = async (_, token) =>
        {
            if (handler.BodyStarts.Count == concurrency) atCapacity.TrySetResult();
            await releaseBodies.Task.WaitAsync(token);
        };
        using var producer = await BuildConcurrentOriginalProducer(handler, concurrency);
        var callbacks = 0;
        producer.OnFileDownloaded += (_, _) => { Interlocked.Increment(ref callbacks); return Task.CompletedTask; };
        var run = RunImageFailureProducer(producer, caller.Token);
        try
        {
            await AwaitOriginalSignal(atCapacity, run);
            Assert.AreEqual(concurrency, Volatile.Read(ref handler.ActiveBodies));
            Assert.AreEqual(concurrency, handler.BodyStarts.Count,
                "The next image must wait for a configured download slot, including while bodies are pending.");
            releaseBodies.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(concurrency, handler.MaximumActiveBodies,
                "Free and paid originals must overlap, while concurrency=1 must remain sequential.");
            Assert.AreEqual(0, Volatile.Read(ref handler.ActiveBodies));
            Assert.AreEqual(imageCount, handler.OriginalRequests);
            Assert.AreEqual(imageCount, handler.BeforeSendChecks);
            Assert.AreEqual(paid ? imageCount : 0, handler.Gallery.BalanceRequests);
            Assert.AreEqual(imageCount, callbacks);
            var result = (await _results.GetByTaskAsync(10)).Single();
            var files = JsonSerializer.Deserialize<string[]>(result.FilesJson)!;
            Assert.AreEqual(imageCount, files.Length);
            foreach (var file in files)
                CollectionAssert.AreEqual(ImageFailureHandler.Png, await File.ReadAllBytesAsync(file));

            var originalRequests = handler.OriginalRequests;
            await RunImageFailureProducer(producer, caller.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(originalRequests, handler.OriginalRequests,
                "Concurrent original output must retain the existing durable completion and resume behavior.");
            Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        }
        finally { await DrainConcurrentOriginal(run, caller, releaseBodies); }
    }

    [TestMethod]
    public async Task OriginalImages_AFreePageCannotReplaceAPaidPagesPreflightState()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var paidQueued = NewOriginalSignal();
        var laterFreeQueued = NewOriginalSignal();
        var releaseSend = NewOriginalSignal();
        var bothBodies = NewOriginalSignal();
        var releaseBodies = NewOriginalSignal();
        using var handler = new ConcurrentOriginalHandler(3, paid: false);
        // Missing Date on page one requires a paid preflight; pages two and three are confirmed free.
        handler.PagesWithoutServerDate.Add(1);
        handler.BeforeOriginalSend = async (page, token) =>
        {
            if (page == 1) paidQueued.TrySetResult();
            if (page == 3) laterFreeQueued.TrySetResult();
            if (page != 2) await releaseSend.Task.WaitAsync(token);
        };
        handler.BeforeBodyRead = async (page, token) =>
        {
            if (page == 2)
            {
                // Page three only gets a slot after the paid preflight finished. Its free decision
                // then happens later than page one's, without blocking the client's HTML gate.
                await paidQueued.Task.WaitAsync(token);
                return;
            }
            if (Volatile.Read(ref handler.ActiveBodies) == 2) bothBodies.TrySetResult();
            await releaseBodies.Task.WaitAsync(token);
        };
        using var producer = await BuildConcurrentOriginalProducer(handler, 2);
        var run = RunImageFailureProducer(producer, caller.Token);
        try
        {
            await AwaitOriginalSignal(laterFreeQueued, run);
            Assert.AreEqual(1, handler.Gallery.BalanceRequests);
            releaseSend.TrySetResult();
            await AwaitOriginalSignal(bothBodies, run);
            releaseBodies.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(3, handler.OriginalRequests,
                "A later free preflight must not make the queued paid page require a free-window proof.");
            Assert.AreEqual(3, handler.BeforeSendChecks);
            Assert.AreEqual(2, handler.MaximumActiveBodies);
            Assert.AreEqual(3, JsonSerializer.Deserialize<string[]>((await _results.GetByTaskAsync(10)).Single().FilesJson)!.Length);
        }
        finally { await DrainConcurrentOriginal(run, caller, releaseSend, releaseBodies); }
    }

    [TestMethod]
    public async Task OriginalImages_APaidPageCannotReplaceAFreePagesStateOrBypassRevokedPermission()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var freeQueued = NewOriginalSignal();
        var paidQueued = NewOriginalSignal();
        var releaseFree = NewOriginalSignal();
        var releasePaid = NewOriginalSignal();
        var freeRecorded = NewOriginalSignal();
        using var handler = new ConcurrentOriginalHandler(3, paid: false);
        handler.PagesWithoutServerDate.Add(3);
        handler.BeforeBodyRead = async (page, token) =>
        {
            // Wait outside the HTML gate so either initial worker may fetch its page first.
            // Completing page two then admits the paid page after page one's free preflight.
            if (page == 2) await freeQueued.Task.WaitAsync(token);
        };
        handler.BeforeOriginalSend = async (page, token) =>
        {
            if (page == 1)
            {
                freeQueued.TrySetResult();
                await releaseFree.Task.WaitAsync(token);
            }
            else if (page == 3)
            {
                paidQueued.TrySetResult();
                await releasePaid.Task.WaitAsync(token);
            }
        };
        ExHentaiOptions options = null!;
        using var producer = await BuildConcurrentOriginalProducer(handler, 2, current => options = current);
        producer.OnFileDownloaded += (path, _) =>
        {
            if (Path.GetFileName(path) == "001.png") freeRecorded.TrySetResult();
            return Task.CompletedTask;
        };
        var run = RunImageFailureProducer(producer, caller.Token);
        try
        {
            await AwaitOriginalSignal(paidQueued, run);
            options.AllowOriginalImageGpSpending = false;
            releaseFree.TrySetResult();
            await AwaitOriginalSignal(freeRecorded, run);
            releasePaid.TrySetResult();
            var error = await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() =>
                run.WaitAsync(TimeSpan.FromSeconds(5)));

            StringAssert.Contains(error.Message, "GP permission or limits changed");
            CollectionAssert.AreEqual(new[] {2, 1}, handler.SentPages.ToArray());
            Assert.AreEqual(2, handler.BeforeSendChecks);
            Assert.AreEqual(1, handler.Gallery.BalanceRequests);
            Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
            Assert.AreEqual(2, Directory.GetFiles(_root, "*.png", SearchOption.AllDirectories).Length,
                "The free page can finish, while the paid page still honors the final permission check.");
        }
        finally { await DrainConcurrentOriginal(run, caller, releaseFree, releasePaid); }
    }

    private async Task<ExHentaiSingleWorkDownloader> BuildConcurrentOriginalProducer(ConcurrentOriginalHandler handler,
        int concurrency, Action<ExHentaiOptions>? configure = null)
    {
        var provider = await TestServiceBuilder.BuildServiceProvider(services => services.AddSingleton(_results));
        var options = provider.GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value;
        OriginalOptions(options);
        options.NamingConvention = "{GalleryId}/{PageTitle}{Extension}";
        options.MaxConcurrency = concurrency;
        options.AllowOriginalImageGpSpending = true;
        configure?.Invoke(options);
        var client = new ExHentaiClient(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
        return new ExHentaiSingleWorkDownloader(provider, provider.GetRequiredService<IStringLocalizer<SharedResource>>(),
            client, provider.GetRequiredService<ITextVocabularyService>(),
            provider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());
    }

    private static TaskCompletionSource NewOriginalSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AwaitOriginalSignal(TaskCompletionSource signal, Task run)
    {
        var finished = await Task.WhenAny(signal.Task, run).WaitAsync(TimeSpan.FromSeconds(5));
        if (finished == run) await run;
        Assert.IsTrue(signal.Task.IsCompleted, "The image batch ended before reaching the controlled concurrency point.");
    }

    private static async Task DrainConcurrentOriginal(Task run, CancellationTokenSource caller,
        params TaskCompletionSource[] signals)
    {
        await caller.CancelAsync();
        foreach (var signal in signals) signal.TrySetResult();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* Release and drain the fixture even when a behavior assertion fails. */ }
    }

    private sealed class ConcurrentOriginalHandler : HttpMessageHandler
    {
        internal readonly GalleryHandler Gallery;
        private readonly HttpMessageInvoker _gallery;
        internal readonly HashSet<int> PagesWithoutServerDate = [];
        internal readonly ConcurrentQueue<int> SentPages = new();
        internal readonly ConcurrentQueue<int> BodyStarts = new();
        internal Func<int, CancellationToken, Task>? BeforeOriginalSend;
        internal Func<int, CancellationToken, Task>? BeforeBodyRead;
        internal int OriginalRequests;
        internal int BeforeSendChecks;
        internal int ActiveBodies;
        internal int MaximumActiveBodies;

        internal ConcurrentOriginalHandler(int pages, bool paid)
        {
            Gallery = new GalleryHandler
            {
                ImagesPerPage = pages, OriginalLinkSize = "1 MB",
                Posted = DateTimeOffset.UtcNow.AddDays(paid ? -400 : -1).ToUnixTimeSeconds()
            };
            _gallery = new HttpMessageInvoker(Gallery);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/fullimg.php")
            {
                var page = int.Parse(uri.Query.TrimStart('?').Split('&')
                    .Single(pair => pair.StartsWith("page=", StringComparison.Ordinal))[5..]);
                if (BeforeOriginalSend != null) await BeforeOriginalSend(page, ct);
                ct.ThrowIfCancellationRequested();
                Assert.IsTrue(request.Options.TryGetValue(ThirdPartyRequestOptions.BeforeSend, out var beforeSend),
                    "Every original request must retain its final send-time financial check.");
                await beforeSend!(ct);
                Interlocked.Increment(ref BeforeSendChecks);
                Interlocked.Increment(ref OriginalRequests);
                SentPages.Enqueue(page);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ConcurrentOriginalContent(ImageFailureHandler.Png, async (stream, token) =>
                    {
                        var active = Interlocked.Increment(ref ActiveBodies);
                        int previous;
                        do { previous = Volatile.Read(ref MaximumActiveBodies); }
                        while (active > previous && Interlocked.CompareExchange(ref MaximumActiveBodies, active, previous) != previous);
                        BodyStarts.Enqueue(page);
                        try
                        {
                            if (BeforeBodyRead != null) await BeforeBodyRead(page, token);
                            await stream.WriteAsync(ImageFailureHandler.Png.AsMemory(), token);
                        }
                        finally { Interlocked.Decrement(ref ActiveBodies); }
                    })
                };
            }
            var imagePage = uri.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal);
            var index = imagePage ? int.Parse(uri.Segments.Last().Split('-').Last()) : 0;
            var response = await _gallery.SendAsync(request, ct);
            if (imagePage)
            {
                var html = await response.Content.ReadAsStringAsync(ct);
                response.Content.Dispose();
                response.Content = new StringContent(html.Replace("&amp;page=1&amp;", $"&amp;page={index}&amp;", StringComparison.Ordinal));
                if (PagesWithoutServerDate.Contains(index)) response.Headers.Date = null;
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _gallery.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ConcurrentOriginalContent(byte[] bytes,
        Func<Stream, CancellationToken, Task> write) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => write(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            write(stream, cancellationToken);
    }
}
