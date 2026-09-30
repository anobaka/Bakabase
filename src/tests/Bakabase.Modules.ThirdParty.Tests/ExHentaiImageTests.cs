using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiImageTests
{
    private const string PageUrl = "https://exhentai.org/s/abcdef0123/12345-1";
    private const string ImageUrl = "https://images.hath.network/image/original.png";
    private const string Cookie = "ipb_member_id=123; ipb_pass_hash=test_hash; igneous=test_igneous";
    private static readonly byte[] ImageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3];
    private static ExHentaiRequestContext Context() => new(Cookie);

    private static string Page(string? original = null, string label = "Download original 1600 x 2200 3.08 MB source") =>
        $"<div id='i3'><img id='img' src='{ImageUrl}'></div>" +
        (original == null ? "" : $"<div id='i7'><a href='{WebUtility.HtmlEncode(original)}'>{label}</a></div>");

    [TestMethod]
    public async Task OriginalDownload_RequiresASpendingCallbackBeforeAnyRequest()
    {
        var handler = new Handler(_ => Html(Page("/fullimg.php?gid=12345&page=1")));
        var client = Client(handler);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions {PreferOriginal = true, RequestContext = Context()}));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("/fullimg.php?gid=12345&page=1", "Download original 1600 x 2200 3.08 MB source", 3240100L)]
    [DataRow("/%66ullimg.php?gid=12345&page=1", "Download original 1600 x 2200 3.08 MB source", 3240100L)]
    [DataRow("/fullimg/12345/1/original.png", "Download original 1600 x 2200 512 KB source", 525312L)]
    [DataRow("/fullimg%2F12345/1/original.png", "Download original 1600 x 2200 512 KB source", 525312L)]
    [DataRow("/fullimg/12345/1/original.png", "Download original 1600 x 2200 100 bytes source", 100L)]
    public async Task OriginalDownload_ChecksTheDisplayedSizeBeforeFollowingThePaidEntry(string original, string label,
        long expectedUpperBound)
    {
        var callbackDone = false;
        ExHentaiOriginalImageInfo? info = null;
        var context = Context();
        var handler = new Handler(request =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/s/")) return Html(Page(original, label));
            if (request.Uri.Host == "exhentai.org")
            {
                Assert.IsTrue(callbackDone, "The spending check must finish before fullimg is requested.");
                return Redirect(ImageUrl);
            }
            return Image();
        });
        var result = await Client(handler).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true, RequestContext = context,
            BeforeOriginalSend = (_, _) => Task.CompletedTask,
            BeforeOriginalDownload = (value, _) =>
            {
                info = value;
                callbackDone = true;
                return Task.CompletedTask;
            }
        });
        Assert.IsTrue(result.IsOriginal);
        Assert.AreEqual(expectedUpperBound, info!.OriginalSizeBytes);
        Assert.AreEqual(new Uri(new Uri(PageUrl), original).AbsoluteUri, info.OriginalUrl);
        CollectionAssert.AreEqual(ImageBytes, result.Data);
        Assert.AreEqual(Cookie, handler.Requests[0].Cookie);
        Assert.AreEqual(Cookie, handler.Requests[1].Cookie);
        Assert.AreEqual(context.AccountKey + ":exhentai.org", handler.Requests[1].AccountKey);
        Assert.AreEqual("", handler.Requests[2].CookieOverride);
        Assert.IsTrue(handler.Requests[2].SuppressSensitiveHeaders);
        Assert.IsTrue(handler.Requests.All(r => r.SkipConfiguredHeaders));
    }

    [TestMethod]
    public async Task SpendingDenialAndUnknownSizeNeverCauseAnUncheckedFullimgRequest()
    {
        var handler = new Handler(_ => Html(Page("/fullimg.php?gid=12345", "Download original source")));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = Context(),
                BeforeOriginalSend = (_, _) => Task.CompletedTask,
                BeforeOriginalDownload = (info, _) =>
                {
                    Assert.IsNull(info.OriginalSizeBytes);
                    throw new InvalidOperationException("Unknown original size.");
                }
            }));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(r => !r.Uri.AbsolutePath.StartsWith("/fullimg")));
    }

    [TestMethod]
    public async Task OriginalWithoutAFullimgEntryUsesDisplayedBytesWithoutClaimingVerifiedOriginal()
    {
        var checks = 0;
        var handler = new Handler(request => request.Uri.AbsolutePath.StartsWith("/s/") ? Html(Page()) : Image());
        var result = await Client(handler).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true, RequestContext = Context(),
            BeforeOriginalSend = (_, _) => Task.CompletedTask,
            BeforeOriginalDownload = (_, _) => { checks++; return Task.CompletedTask; }
        });
        Assert.IsFalse(result.IsOriginal);
        Assert.IsTrue(result.OriginalUnavailable);
        CollectionAssert.AreEqual(ImageBytes, result.Data);
        Assert.AreEqual(0, checks);
    }

    [TestMethod]
    public async Task NormalDownloadKeepsTheLegacyTupleAndUsesDisplayedBytes()
    {
        var handler = new Handler(request => request.Uri.AbsolutePath.StartsWith("/s/")
            ? Html(Page("/fullimg.php?gid=12345")) : Image());
        var result = await Client(handler).DownloadImage(PageUrl);
        CollectionAssert.AreEqual(ImageBytes, result.Data);
        Assert.AreEqual("image/png", result.ContentType);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(r => !r.Uri.AbsolutePath.StartsWith("/fullimg")));
    }

    [TestMethod]
    public async Task EveryFullimgRedirectIsCheckedAndLoopsAreRejected()
    {
        var checks = new List<string>();
        var handler = new Handler(request =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/s/")) return Html(Page("/fullimg.php?gid=12345"));
            return request.Uri.AbsolutePath == "/fullimg.php"
                ? Redirect("/fullimg/12345/1/original.png") : Redirect("/fullimg.php?gid=12345");
        });
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = Context(),
                BeforeOriginalSend = (_, _) => Task.CompletedTask,
                BeforeOriginalDownload = (info, _) => { checks.Add(info.OriginalUrl); return Task.CompletedTask; }
            }));
        Assert.AreEqual(2, checks.Count);
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task AFinalSpendingCheckCanAbortAfterRequestPreparationBeforeThePaidRequestIsSent()
    {
        var paidRequests = 0;
        var preflight = 0;
        var finalChecks = 0;
        var handler = new Handler(request =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/s/")) return Html(Page("/fullimg.php?gid=12345"));
            paidRequests++;
            return Image();
        });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = Context(),
                BeforeOriginalDownload = (_, _) => { preflight++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) =>
                {
                    finalChecks++;
                    throw new InvalidOperationException("The free window ended while waiting for HTTP pacing.");
                }
            }));
        Assert.AreEqual(1, preflight);
        Assert.AreEqual(1, finalChecks);
        Assert.AreEqual(0, paidRequests);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow(-300)]
    [DataRow(300)]
    public async Task MissingOrSkewedPageDateCannotCertifyAServerTime(int? offsetSeconds)
    {
        var handler = new Handler(request =>
        {
            if (!request.Uri.AbsolutePath.StartsWith("/s/")) return Image();
            var response = Html(Page("/fullimg.php?gid=12345"));
            if (offsetSeconds.HasValue) response.Headers.Date = DateTimeOffset.UtcNow.AddSeconds(offsetSeconds.Value);
            return response;
        });
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = Context(),
                BeforeOriginalDownload = (info, _) =>
                {
                    Assert.IsNull(info.ServerTimeUtc);
                    throw new InvalidOperationException("Server time could not establish a free request.");
                },
                BeforeOriginalSend = (_, _) => Task.CompletedTask
            }));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task FinalCheckRebuildsServerTimeUsingMonotonicElapsedTime()
    {
        ExHentaiOriginalImageInfo? preflight = null;
        ExHentaiOriginalImageInfo? final = null;
        var handler = new Handler(request =>
        {
            if (!request.Uri.AbsolutePath.StartsWith("/s/")) return Image();
            var response = Html(Page("/fullimg.php?gid=12345"));
            response.Headers.Date = DateTimeOffset.UtcNow;
            return response;
        });
        await Client(handler).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true, RequestContext = Context(),
            BeforeOriginalDownload = async (info, token) => { preflight = info; await Task.Delay(30, token); },
            BeforeOriginalSend = (info, _) => { final = info; return Task.CompletedTask; }
        });
        Assert.IsNotNull(preflight!.ServerTimeUtc);
        Assert.IsNotNull(final!.ServerTimeUtc);
        Assert.AreEqual(DateTimeKind.Utc, final.ServerTimeUtc.Value.Kind);
        Assert.IsTrue(final.ServerTimeUtc.Value - preflight.ServerTimeUtc!.Value >= TimeSpan.FromMilliseconds(20));
    }

    [DataTestMethod]
    [DataRow("login")]
    [DataRow("html")]
    [DataRow("limit")]
    [DataRow("invalid")]
    [DataRow("status")]
    public async Task DownloadImageRejectsAccessErrorsInsteadOfSavingThem(string error)
    {
        var handler = new Handler(request =>
        {
            if (request.Uri.AbsolutePath.StartsWith("/s/"))
                return error == "login" ? Html("<form><input type='password'></form>") :
                    error == "limit" ? Html("<img id='img' src='https://ehgt.org/g/509.gif'>") : Html(Page());
            if (error == "html") return Html("Downloading original files of this gallery requires GP.");
            if (error == "status") return new HttpResponseMessage(HttpStatusCode.Forbidden);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"))};
        });
        if (error == "status")
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Client(handler).DownloadImage(PageUrl));
        else
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        if (error == "limit" || error == "login") Assert.AreEqual(1, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("1,234 kGP", "56,789 Credits", 1234000L, 56789L)]
    [DataRow("0 kGP", "0 Credits", 0L, 0L)]
    [DataRow("123,456 GP", "20 Credits", 123456L, 20L)]
    [DataRow("1.234 kGP", "20 Credits", 1234L, 20L)]
    public async Task BalanceReadsAvailableFundsFromExchangeLabels(string gp, string credits, long expectedGp,
        long expectedCredits)
    {
        // Format confirmed against EhViewer HomeParser.parseFunds; no authenticated live request.
        var handler = new Handler(_ => Html($"<div class='stuffbox'><h2>GP Exchange</h2>" +
            $"<div>Available: <strong>{credits}</strong></div><div>Available: <strong>{gp}</strong></div>" +
            "<div>Buy price: 99,999 kGP</div></div>"));
        var context = Context();
        var balance = await Client(handler).GetAccountBalance(context);
        Assert.AreEqual(expectedGp, balance.GpBalance);
        Assert.AreEqual(expectedCredits, balance.CreditsBalance);
        Assert.AreEqual(ExHentaiClient.AccountBalanceUrl, handler.Requests.Single().Uri.AbsoluteUri);
        Assert.AreEqual(Cookie, handler.Requests.Single().Cookie);
        Assert.AreEqual(context.AccountKey + ":e-hentai.org", handler.Requests.Single().AccountKey);
    }

    [TestMethod]
    public async Task BalanceAndImagePageUseTheSameSnapshotAcrossBothAccountHosts()
    {
        var context = Context();
        var handler = new Handler(request => request.Uri.AbsolutePath == "/exchange.php"
            ? Html("<div class='stuffbox'>Available: 10 kGP</div>") : Html(Page("/fullimg.php?gid=12345")));
        var client = Client(handler);
        await client.GetAccountBalance(context);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = context,
                BeforeOriginalSend = (_, _) => Task.CompletedTask,
                BeforeOriginalDownload = (_, _) => throw new InvalidOperationException("Stop before original")
            }));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(r => r.Cookie == Cookie));
        Assert.AreEqual(context.AccountKey + ":e-hentai.org", handler.Requests[0].AccountKey);
        Assert.AreEqual(context.AccountKey + ":exhentai.org", handler.Requests[1].AccountKey);
    }

    [TestMethod]
    public async Task BalanceSeparatesAdjacentTableCellsAndIgnoresScriptCurrencyLiterals()
    {
        var handler = new Handler(_ => Html("<script>const message='Available: 999 kGP';</script>" +
            "<style>.fake:after{content:'Available: 888 kGP'}</style><div class='stuffbox'><table>" +
            "<tr><td>Available:</td><td><strong>1,234</strong> Credits</td></tr>" +
            "<tr><td>Available:</td><td><strong>2</strong> kGP</td></tr></table></div>"));
        var balance = await Client(handler).GetAccountBalance(Context());
        Assert.AreEqual(2000L, balance.GpBalance);
        Assert.AreEqual(1234L, balance.CreditsBalance);
    }

    [DataTestMethod]
    [DataRow("<form><input type='password'></form>Available: 10 kGP")]
    [DataRow("<div>Last trade: 999 kGP</div>")]
    [DataRow("<div>Available: 10 kGP; Available: 20 kGP</div>")]
    public async Task BalanceFailsExplicitlyWhenLoginOrFundsCannotBeConfirmed(string html)
    {
        var handler = new Handler(_ => Html(html));
        try
        {
            await Client(handler).GetAccountBalance(Context());
            Assert.Fail("An unconfirmed balance must not be returned.");
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { }
    }

    [TestMethod]
    public async Task MissingCookieAndPrecancelledRequestsDoNotReachTheServer()
    {
        var handler = new Handler(_ => Html(Page()));
        var client = Client(handler);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetAccountBalance());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions(), cts.Token));
        Assert.AreEqual(0, handler.Requests.Count);
        var context = Context();
        Assert.AreEqual(context.AccountKey, Context().AccountKey);
        Assert.IsFalse(JsonSerializer.Serialize(context).Contains("test_hash"));
    }

    private static ExHentaiClient Client(Handler handler) => new(new Factory(new HttpClient(handler)),
        NullLoggerFactory.Instance);
    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
        {Content = new StringContent(html, Encoding.UTF8, "text/html")};
    private static HttpResponseMessage Image()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(ImageBytes)};
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }
    private static HttpResponseMessage Redirect(string destination)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(destination, UriKind.RelativeOrAbsolute);
        return response;
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed record Request(Uri Uri, string? Cookie, string? CookieOverride, string? AccountKey,
        bool SkipConfiguredHeaders, bool SuppressSensitiveHeaders);
    private sealed class Handler(Func<Request, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<Request> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            request.Options.TryGetValue(ThirdPartyRequestOptions.Cookie, out var cookieOverride);
            request.Options.TryGetValue(ThirdPartyRequestOptions.AccountKey, out var accountKey);
            request.Options.TryGetValue(ThirdPartyRequestOptions.SkipConfiguredHeaders, out var skip);
            request.Options.TryGetValue(ThirdPartyRequestOptions.SuppressSensitiveHeaders, out var suppress);
            var record = new Request(request.RequestUri!, request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values) : null, cookieOverride, accountKey, skip, suppress);
            Requests.Add(record);
            if (request.Options.TryGetValue(ThirdPartyRequestOptions.BeforeSend, out var beforeSend)) await beforeSend(ct);
            return respond(record);
        }
    }
}
