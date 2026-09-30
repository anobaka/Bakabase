using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

/// <summary>
/// Every ExHentai page load in the app goes through one retrying, gated fetch. Its retry used to
/// jump back into its own try block, releasing the one-permit gate once per attempt: the retry meant
/// to absorb a dropped connection ended in a SemaphoreFullException instead.
/// </summary>
[TestClass]
public sealed class ExHentaiClientRetryTests
{
    private const string PageUrl = "https://exhentai.org/s/abc/12345-1";
    private static readonly byte[] ImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAX+XDSwAAAABJRU5ErkJggg==");

    [TestMethod]
    public async Task ADroppedConnectionIsRetriedAndTheGateStillWorksAfterwards()
    {
        var handler = new ScriptedHandler { PageFailures = 1 };
        var client = BuildClient(handler);

        var first = await client.DownloadImage(PageUrl);
        CollectionAssert.AreEqual(ImageBytes, first.Data);
        Assert.AreEqual(2, handler.PageRequests);

        // With the old gate bug this second call either threw SemaphoreFullException or, with a
        // concurrent caller, let two requests through a gate meant for one.
        var second = await client.DownloadImage(PageUrl);
        CollectionAssert.AreEqual(ImageBytes, second.Data);
        Assert.AreEqual(3, handler.PageRequests);
    }

    [TestMethod]
    public async Task APersistentOutageGivesUpAfterThreeAttemptsAndReleasesTheGate()
    {
        var handler = new ScriptedHandler { PageFailures = int.MaxValue };
        var client = BuildClient(handler);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.DownloadImage(PageUrl));
        Assert.AreEqual(HttpRequestError.SecureConnectionError, error.HttpRequestError);
        Assert.AreEqual(3, handler.PageRequests);

        handler.PageFailures = 0;
        var recovered = await client.DownloadImage(PageUrl).WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(ImageBytes, recovered.Data);
    }

    [TestMethod]
    public async Task APageTimeoutUsesTheSameBoundedRetryPolicy()
    {
        var handler = new ScriptedHandler
        {
            PageFailures = int.MaxValue,
            PageFailure = () => new TaskCanceledException("The request timed out.", new TimeoutException())
        };
        var client = BuildClient(handler);

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => client.DownloadImage(PageUrl));
        Assert.AreEqual(3, handler.PageRequests);
        handler.PageFailures = 0;
        var recovered = await client.DownloadImage(PageUrl).WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(ImageBytes, recovered.Data);
    }

    [TestMethod]
    public async Task AListPageDoesNotNestTwoThreeAttemptRetryLoops()
    {
        var handler = new ScriptedHandler {PageFailures = int.MaxValue};
        var client = BuildClient(handler);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.ParseList(
            "https://exhentai.org/?f_search=test", includeMetadata: false));
        Assert.AreEqual(3, handler.PageRequests);
    }

    [TestMethod]
    public async Task StoppingAQueuedPageDoesNotReleaseAnotherRequestsPermit()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler
        {
            OnPageRequest = async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        var client = BuildClient(handler);
        var first = client.DownloadImage(PageUrl);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            var queued = client.DownloadImage(PageUrl, cts.Token);
            await cts.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, handler.PageRequests, "The queued call must not send or release the first call's permit.");
        }
        finally
        {
            release.TrySetResult();
        }
        CollectionAssert.AreEqual(ImageBytes, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Data);
        CollectionAssert.AreEqual(ImageBytes, (await client.DownloadImage(PageUrl)).Data);
        Assert.AreEqual(2, handler.PageRequests);
    }

    [TestMethod]
    public async Task AFullimgFailureRequiresNewSpendingChecksBeforeAnotherRequest()
    {
        var handler = new ScriptedHandler
        {
            FullimgFailures = 1,
            PageHtml = "<img id='img' src='https://exhentai.org/image/1.png' />" +
                       "<a href='/fullimg.php?gid=12345&amp;page=1'>Download original 1 x 1 100 bytes source</a>"
        };
        var client = BuildClient(handler);
        var checks = 0;
        var finalChecks = 0;
        var options = new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true,
            RequestContext = new ExHentaiRequestContext("ipb_member_id=123; ipb_pass_hash=test_hash"),
            BeforeOriginalDownload = (_, _) => {checks++; return Task.CompletedTask;},
            BeforeOriginalSend = (_, _) => {finalChecks++; return Task.CompletedTask;}
        };

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.DownloadImage(PageUrl, options));
        Assert.AreEqual(1, handler.PageRequests);
        Assert.AreEqual(1, handler.FullimgRequests, "The page retry helper must not retry a possibly charged request.");
        Assert.AreEqual(1, checks);
        Assert.AreEqual(1, finalChecks);

        var recovered = await client.DownloadImage(PageUrl, options);
        Assert.IsTrue(recovered.IsOriginal);
        CollectionAssert.AreEqual(ImageBytes, recovered.Data);
        Assert.AreEqual(2, handler.FullimgRequests);
        Assert.AreEqual(2, checks, "An explicit retry must run the spending preflight again.");
        Assert.AreEqual(2, finalChecks);
    }

    [TestMethod]
    public async Task ABanIsNeverRetried()
    {
        // Every retry is another page load, and page loads are what the ban is counting.
        var handler = new ScriptedHandler
        {
            PageHtml = "Your IP address has been temporarily banned for excessive pageloads."
        };
        var client = BuildClient(handler);

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.DownloadImage(PageUrl));
        StringAssert.Contains(error.Message, "access banned or rate limited");
        Assert.IsFalse(error.Message.Contains(handler.PageHtml, StringComparison.Ordinal),
            "Safe diagnostics must not return the response body.");
        Assert.AreEqual(1, handler.PageRequests);
    }

    [TestMethod]
    public async Task AMissingPageIsNeverRetried()
    {
        var handler = new ScriptedHandler { PageStatus = HttpStatusCode.NotFound };
        var client = BuildClient(handler);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.DownloadImage(PageUrl));
        Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
        Assert.AreEqual(1, handler.PageRequests);
    }

    [TestMethod]
    public async Task StoppingDuringTheBackoffEndsTheWaitAtOnce()
    {
        var handler = new ScriptedHandler { PageFailures = int.MaxValue };
        var client = BuildClient(handler);
        using var cts = new CancellationTokenSource();
        // Cancel well after the failure has been judged retryable, while the backoff (0.8s at the
        // least) is under way.
        handler.OnPageFailure = () => cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => client.DownloadImage(PageUrl, cts.Token));
        elapsed.Stop();
        Assert.AreEqual(1, handler.PageRequests);
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(700),
            $"Stopping took {elapsed.Elapsed.TotalMilliseconds}ms: the backoff was waited out instead of interrupted.");
    }

    private static ExHentaiClient BuildClient(HttpMessageHandler handler) =>
        new(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public int PageFailures;
        public int PageRequests;
        public int FullimgFailures;
        public int FullimgRequests;
        public string? PageHtml;
        public HttpStatusCode PageStatus = HttpStatusCode.OK;
        public Action? OnPageFailure;
        public Func<Exception> PageFailure = Eof;
        public Func<CancellationToken, Task>? OnPageRequest;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Options.TryGetValue(ThirdPartyRequestOptions.BeforeSend, out var beforeSend))
                await beforeSend(ct);
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.StartsWith("/s/") || uri.AbsolutePath == "/")
            {
                PageRequests++;
                if (OnPageRequest != null) await OnPageRequest(ct);
                if (PageFailures > 0)
                {
                    PageFailures--;
                    OnPageFailure?.Invoke();
                    throw PageFailure();
                }

                return new HttpResponseMessage(PageStatus)
                {
                    Content = new StringContent(PageHtml ?? "<img id='img' src='https://exhentai.org/image/1.jpg' />")
                };
            }

            if (uri.AbsolutePath.StartsWith("/fullimg"))
            {
                FullimgRequests++;
                if (FullimgFailures-- > 0) throw Eof();
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ImageBytes)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }

        private static Exception Eof() => new HttpRequestException(HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established, see inner exception.",
            new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));
    }
}
