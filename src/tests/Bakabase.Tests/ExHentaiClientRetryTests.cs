using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
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

    [TestMethod]
    public async Task ADroppedConnectionIsRetriedAndTheGateStillWorksAfterwards()
    {
        var handler = new ScriptedHandler { PageFailures = 1 };
        var client = BuildClient(handler);

        var first = await client.DownloadImage(PageUrl);
        Assert.AreEqual("image", Encoding.UTF8.GetString(first.Data));
        Assert.AreEqual(2, handler.PageRequests);

        // With the old gate bug this second call either threw SemaphoreFullException or, with a
        // concurrent caller, let two requests through a gate meant for one.
        var second = await client.DownloadImage(PageUrl);
        Assert.AreEqual("image", Encoding.UTF8.GetString(second.Data));
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
        Assert.AreEqual("image", Encoding.UTF8.GetString(recovered.Data));
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

        await Assert.ThrowsExactlyAsync<Exception>(() => client.DownloadImage(PageUrl));
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
        public string? PageHtml;
        public HttpStatusCode PageStatus = HttpStatusCode.OK;
        public Action? OnPageFailure;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.StartsWith("/s/"))
            {
                PageRequests++;
                if (PageFailures > 0)
                {
                    PageFailures--;
                    OnPageFailure?.Invoke();
                    throw new HttpRequestException(HttpRequestError.SecureConnectionError,
                        "The SSL connection could not be established, see inner exception.",
                        new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));
                }

                return Task.FromResult(new HttpResponseMessage(PageStatus)
                {
                    Content = new StringContent(PageHtml ?? "<img id='img' src='https://exhentai.org/image/1.jpg' />")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("image"))
            });
        }
    }
}
