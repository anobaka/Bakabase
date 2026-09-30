using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Components.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Bootstrap.Components.Configuration.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiImageRecoveryTests
{
    private const string PageUrl = "https://exhentai.org/s/abcdef0123/4170203-46";
    private const string ImageUrl = "https://first.hath.network/h/signed-image-secret/page.png?key=signed-query-secret";
    private const string SecondImageUrl = "https://second.hath.network/h/replacement-image-secret/page.png?key=another-secret";
    private const string ReloadToken = "12345-NLSecretabcdef";
    private const string Cookie = "ipb_member_id=123; ipb_pass_hash=synthetic-cookie-secret; igneous=synthetic-igneous";

    [DataTestMethod]
    [DataRow("PNG", "image/png")]
    [DataRow("JPEG", "image/jpeg")]
    [DataRow("GIF", "image/gif")]
    [DataRow("WebP", "image/webp")]
    public async Task OrdinaryDownloadPreservesAllOfficialImageFormatsWithoutReencoding(string format, string mediaType)
    {
        var bytes = EncodedImage(format);
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page()) : Image(bytes, mediaType));
        var result = await Client(handler).DownloadImage(PageUrl);
        CollectionAssert.AreEqual(bytes, result.Data);
        Assert.AreEqual(mediaType, result.ContentType);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("html")]
    [DataRow("empty")]
    [DataRow("invalid")]
    [DataRow("403")]
    [DataRow("404")]
    [DataRow("503")]
    [DataRow("transport")]
    [DataRow("bodyTransport")]
    [DataRow("headerTimeout")]
    [DataRow("bodyTimeout")]
    public async Task AnOrdinaryNodeFailureReloadsTheViewingPageOnceAndUsesItsNewImage(string failure)
    {
        var bytes = EncodedImage("PNG");
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/"))
                return Html(Page(request.Uri.Query.Contains("nl=") ? SecondImageUrl : ImageUrl));
            if (request.Uri.Host == "second.hath.network") return Image(bytes);
            return FailedResponse(failure);
        });
        using var http = new HttpClient(handler)
            {Timeout = failure == "bodyTimeout" ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(3)};
        var result = await Client(http).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions
            {RequestContext = new ExHentaiRequestContext(Cookie)});
        CollectionAssert.AreEqual(bytes, result.Data);
        Assert.AreEqual(4, handler.Requests.Count);
        var reload = handler.Requests[2];
        Assert.AreEqual(new Uri(PageUrl).Host, reload.Uri.Host);
        Assert.AreEqual(new Uri(PageUrl).LocalPath, reload.Uri.LocalPath);
        Assert.AreEqual("?nl=" + ReloadToken, reload.Uri.Query);
        Assert.AreEqual(PageUrl, reload.LogKey);
        Assert.AreEqual(Cookie, reload.Cookie);
        foreach (var external in handler.Requests.Where(r => r.Uri.Host.EndsWith("hath.network")))
        {
            Assert.IsNull(external.Cookie);
            Assert.AreEqual("", external.CookieOverride);
            Assert.AreEqual("image-external:" + external.Uri.Host, external.AccountKey);
            Assert.IsTrue(external.SkipConfiguredHeaders);
            Assert.IsTrue(external.SuppressSensitiveHeaders);
            Assert.IsFalse(external.LogKey!.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
        Assert.IsFalse(result.IsOriginal);
    }

    [TestMethod]
    public async Task ReloadPreservesUnrelatedQueryValuesAndReplacesOnlyTheExistingNlValue()
    {
        var url = PageUrl + "?p=4&nl=old-secret&foo=a%2Bb&%4e%4c=second-old-secret#anchor";
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page(request.Uri.Query.Contains(ReloadToken) ? SecondImageUrl : ImageUrl,
                "this.onerror=null;nl('" + ReloadToken + "');", includeLoadFail: false))
            : request.Uri.Host == "first.hath.network" ? Html("node unavailable") : Image(EncodedImage("PNG")));
        await Client(handler).DownloadImage(url);
        var reload = handler.Requests[2];
        Assert.AreEqual("?p=4&foo=a%2Bb&nl=" + ReloadToken, reload.Uri.Query);
        Assert.AreEqual("", reload.Uri.Fragment);
        Assert.AreEqual(PageUrl, reload.LogKey);
    }

    [DataTestMethod]
    [DataRow("nl('1')")]
    [DataRow("nl('unknown')")]
    [DataRow("nl(window.secret)")]
    [DataRow("nl('12345-safe');location='https://evil.example/fullimg.php'")]
    [DataRow("location='https://evil.example/?nl=12345-safe'")]
    [DataRow("nl('12345-safe', 'https://evil.example')")]
    public async Task UnknownTokensAndJavascriptExpressionsCannotCauseANodeReload(string onclick)
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page(onclick: onclick)) : Html("node unavailable"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
    }

    [TestMethod]
    public async Task DisagreeingLiteralTokensDoNotChooseAnArbitraryReload()
    {
        var html = Page() + "<script>nl('12345-script-token')</script>";
        html = html.Replace("<img id='img'", "<img onerror=\"nl('12345-differentToken')\" id='img'");
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? Html(html) : Html("node unavailable"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ASecondNodeFailureStopsAndCannotSpendAnotherReloadThroughOuterRetries()
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page(request.Uri.Query.Contains("nl=") ? SecondImageUrl : ImageUrl))
            : Html("private-html-body-secret"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl + "?private=page-query-secret"));
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(new AggregateException(new InvalidOperationException("wrapper", error))));
        AssertSafeDiagnostic(error, "second.hath.network", "200", "text/html");
        Assert.AreEqual(Encoding.UTF8.GetByteCount("private-html-body-secret"),
            ExtractLength(error.Message));
    }

    [TestMethod]
    public async Task AnExhaustedNodeTransportFailureKeepsItsClassificationButCannotTriggerAnotherNl()
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page()) : throw new HttpRequestException(HttpRequestError.ConnectionError, "wire " + ImageUrl));
        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.IsTrue(TransientNetworkError.IsTransient(error));
        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.IsNull(error.InnerException);
        AssertSafeDiagnostic(error, "first.hath.network", "unknown", "unknown");
    }

    [TestMethod]
    public async Task ABlankPlaceholderCanReloadWithoutRequestingThePlaceholderItself()
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/")
            ? Html(Page(request.Uri.Query.Contains("nl=") ? SecondImageUrl : "https://ehgt.org/g/blank.gif"))
            : Image(EncodedImage("PNG")));
        await Client(handler).DownloadImage(PageUrl);
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.LocalPath.EndsWith("blank.gif")));
    }

    [DataTestMethod]
    [DataRow("https://ehgt.org/g/509.gif")]
    [DataRow("https://ehgt.org/g/509s.gif")]
    public async Task ViewingQuotaImagesStopEvenWhenAnOriginalEntryAndReloadTokenArePresent(string placeholder)
    {
        var checks = 0;
        var handler = new Handler(_ => Html(Page(placeholder, original: "/fullimg/123/46/signed-original-secret")));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            OriginalOptions(() => checks++, () => checks++)));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(0, checks);
        StringAssert.Contains(error.Message, "quota");
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
    }

    [DataTestMethod]
    [DataRow("Error 509 - bandwidth exceeded", "quota")]
    [DataRow("Could not get dispatch for image", "quota")]
    [DataRow("Please log in", "login")]
    [DataRow("<form><input type='password'></form>", "login")]
    [DataRow("<form id='challenge-form'>verify you are human</form>", "challenge")]
    [DataRow("Your IP is temporarily banned", "banned")]
    [DataRow("Insufficient GP for downloading this original", "GP")]
    public async Task ConfirmedQuotaLoginChallengeBanAndFundsErrorsNeverReload(string body, string reason)
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? Html(Page()) : Html(body));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(2, handler.Requests.Count);
        StringAssert.Contains(error.Message, reason);
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
    }

    [TestMethod]
    public async Task ALoginViewingPageStopsBeforeRequestingAnyImageOrNodeReload()
    {
        var handler = new Handler(_ => Html("<form><input type=password></form>" + Page()));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(error.Message, "login");
    }

    [DataTestMethod]
    [DataRow("unknown HTML", "HTML response")]
    [DataRow("Insufficient GP", "GP")]
    [DataRow("Error 509 - bandwidth exceeded", "quota")]
    [DataRow("Please log in", "login")]
    public async Task OriginalNodeErrorsNeverRepeatThePaidEntryOrUseOrdinaryNlRecovery(string body, string reason)
    {
        var preflight = 0;
        var final = 0;
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/")) return Html(Page(original: "/fullimg/123/46/signed-original-secret"));
            if (request.Uri.Host == "exhentai.org") return Redirect(ImageUrl);
            return Html(body);
        });
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            OriginalOptions(() => preflight++, () => final++)));
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.AreEqual(1, preflight);
        Assert.AreEqual(1, final);
        Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.LocalPath.StartsWith("/fullimg/")));
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.Query.Contains("nl=")));
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        StringAssert.Contains(error.Message, reason);
        AssertSafeDiagnostic(error, "first.hath.network", "200", "text/html");
    }

    [TestMethod]
    public async Task OriginalBodyTransportFailureRetainsTransientPolicyWithoutRepeatingThePaidEntry()
    {
        var preflight = 0;
        var final = 0;
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/")) return Html(Page(original: "/fullimg.php?private=signed-original-secret"));
            if (request.Uri.Host == "exhentai.org") return Redirect(ImageUrl);
            return FailedResponse("bodyTransport");
        });
        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Client(handler).DownloadImage(PageUrl,
            OriginalOptions(() => preflight++, () => final++)));
        Assert.IsTrue(TransientNetworkError.IsTransient(error));
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.AreEqual(1, preflight);
        Assert.AreEqual(1, final);
        Assert.AreEqual(3, handler.Requests.Count);
        AssertSafeDiagnostic(error, "first.hath.network", "200", "image/png");
        Assert.AreEqual(64L, ExtractLength(error.Message));
    }

    [TestMethod]
    public async Task AConfirmedFreeOriginalWithZeroLeadingBytesCanReplaceItsNodeOnceWithoutReloadingTheViewingPage()
    {
        var preflight = new List<ExHentaiOriginalImageInfo>();
        var final = new List<ExHentaiOriginalImageInfo>();
        var original = "/fullimg/123/46/signed-original-secret?gid=4170203&page=46&nl=oldSecret";
        var bytes = EncodedImage("JPEG");
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/")) return DatedHtml(Page(original: original));
            if (request.Uri.Host == "exhentai.org")
                return Redirect(request.Uri.Query.Contains(ReloadToken) ? SecondImageUrl : ImageUrl);
            return request.Uri.Host == "first.hath.network" ? Image(new byte[64], "image/jpeg") : Image(bytes, "image/jpeg");
        });
        var result = await Client(handler).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
            BeforeOriginalDownload = (info, _) => { preflight.Add(info); return Task.CompletedTask; },
            BeforeOriginalSend = (info, _) => { final.Add(info); return Task.CompletedTask; },
            CanRecoverOriginalWithoutGp = info => info.ServerTimeUtc.HasValue
        });
        Assert.IsTrue(result.IsOriginal);
        Assert.IsFalse(result.OriginalUnavailable);
        CollectionAssert.AreEqual(bytes, result.Data);
        Assert.AreEqual(5, handler.Requests.Count);
        Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.LocalPath.StartsWith("/s/")));
        Assert.AreEqual(2, preflight.Count);
        Assert.AreEqual(2, final.Count);
        Assert.IsTrue(preflight[1].ServerTimeUtc >= preflight[0].ServerTimeUtc);
        var replacement = handler.Requests.Single(r => r.Uri.Query.Contains(ReloadToken));
        Assert.AreEqual("exhentai.org", replacement.Uri.Host);
        Assert.AreEqual("/fullimg/123/46/signed-original-secret", replacement.Uri.LocalPath);
        Assert.AreEqual("?gid=4170203&page=46&nl=" + ReloadToken, replacement.Uri.Query);
        Assert.IsFalse(replacement.LogKey!.Contains(ReloadToken));
        Assert.AreEqual(replacement.Uri.AbsoluteUri, preflight[1].OriginalUrl);
        Assert.AreEqual(replacement.Uri.AbsoluteUri, final[1].OriginalUrl);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalZeroBytesStopWhenFreeRecoveryIsDeniedOrHasNoCallback(bool absentCallback)
    {
        var checks = 0;
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? DatedHtml(Page(original: "/fullimg.php?gid=4170203&page=46")) :
            request.Uri.Host == "exhentai.org" ? Redirect(ImageUrl) : Image(new byte[64], "image/jpeg"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => { checks++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) => { checks++; return Task.CompletedTask; },
                CanRecoverOriginalWithoutGp = absentCallback ? null : _ => false
            }));
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.AreEqual(2, checks);
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        StringAssert.Contains(error.Message, "image data begins with zero bytes");
        AssertSafeDiagnostic(error, "first.hath.network", "200", "image/jpeg");
        Assert.AreEqual(64L, ExtractLength(error.Message));
    }

    [DataTestMethod]
    [DataRow(2, 1)]
    [DataRow(3, 2)]
    public async Task FreeOriginalRecoveryRechecksBeforePreparationAndAgainBeforeSending(int denyCheck, int expectedPreflight)
    {
        var freeChecks = 0;
        var preflight = 0;
        var final = 0;
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? DatedHtml(Page(original: "/fullimg.php?gid=4170203&page=46")) :
            request.Uri.Host == "exhentai.org" ? Redirect(ImageUrl) : Image(new byte[64], "image/jpeg"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => { preflight++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) => { final++; return Task.CompletedTask; },
                CanRecoverOriginalWithoutGp = _ => ++freeChecks < denyCheck
            }));
        Assert.AreEqual(denyCheck, freeChecks);
        Assert.AreEqual(expectedPreflight, preflight);
        Assert.AreEqual(1, final);
        Assert.AreEqual(1, handler.SentRequests.Count(r => r.Uri.LocalPath.StartsWith("/fullimg")));
    }

    [DataTestMethod]
    [DataRow("Insufficient GP")]
    [DataRow("Error 509 - bandwidth exceeded")]
    [DataRow("Please log in")]
    public async Task ConfirmedOriginalRestrictionsNeverInvokeEvenAnEnabledFreeRecovery(string body)
    {
        var freeChecks = 0;
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? DatedHtml(Page(original: "/fullimg.php?gid=4170203&page=46")) :
            request.Uri.Host == "exhentai.org" ? Redirect(ImageUrl) : Html(body));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => Task.CompletedTask,
                BeforeOriginalSend = (_, _) => Task.CompletedTask,
                CanRecoverOriginalWithoutGp = _ => { freeChecks++; return true; }
            }));
        Assert.AreEqual(0, freeChecks);
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ASecondFreeOriginalNodeFailureIsExhaustedAndCannotConsumeAnotherReload()
    {
        var checks = 0;
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? DatedHtml(Page(original: "/fullimg.php?gid=4170203&page=46")) :
            request.Uri.Host == "exhentai.org" ? Redirect(ImageUrl) : Image(new byte[64], "image/jpeg"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => { checks++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) => { checks++; return Task.CompletedTask; },
                CanRecoverOriginalWithoutGp = _ => true
            }));
        Assert.AreEqual(5, handler.Requests.Count);
        Assert.AreEqual(4, checks);
        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        AssertSafeDiagnostic(error, "first.hath.network", "200", "image/jpeg");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalPreferenceWithoutAnEntryCanReloadThePageButCannotUpgradeToANewPaidEntry(bool replacementFails)
    {
        var spendingChecks = 0;
        var freeChecks = 0;
        var bytes = EncodedImage("PNG");
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/"))
                return Html(request.Uri.Query.Contains("nl=")
                    ? Page(SecondImageUrl, original: "/fullimg.php?bait=new-original-secret") : Page());
            if (request.Uri.Host == "exhentai.org")
                throw new InvalidOperationException("The newly exposed paid entry must never be sent.");
            return request.Uri.Host == "first.hath.network" || replacementFails
                ? Html("private-html-body-secret") : Image(bytes);
        });
        var options = new ExHentaiImageDownloadOptions
        {
            PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
            BeforeOriginalDownload = (_, _) => { spendingChecks++; throw new InvalidOperationException("No original was selected."); },
            BeforeOriginalSend = (_, _) => { spendingChecks++; throw new InvalidOperationException("No original was selected."); },
            CanRecoverOriginalWithoutGp = _ => { freeChecks++; return true; }
        };
        if (replacementFails)
        {
            var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl, options));
            Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
            AssertSafeDiagnostic(error, "second.hath.network", "200", "text/html");
        }
        else
        {
            var result = await Client(handler).DownloadImage(PageUrl, options);
            CollectionAssert.AreEqual(bytes, result.Data);
            Assert.IsFalse(result.IsOriginal);
            Assert.IsTrue(result.OriginalUnavailable);
        }
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.AreEqual(2, handler.Requests.Count(r => r.Uri.LocalPath.StartsWith("/s/")));
        Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.Query.Contains("nl=")));
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.LocalPath.StartsWith("/fullimg")));
        Assert.AreEqual(0, spendingChecks);
        Assert.AreEqual(0, freeChecks);
    }

    [TestMethod]
    public async Task ABlankViewingImageWithAnOriginalEntryCannotSilentlySwitchOriginalPreferenceToOrdinaryRecovery()
    {
        var spendingChecks = 0;
        var handler = new Handler(_ => Html(Page("https://ehgt.org/g/blank.gif",
            original: "/fullimg.php?gid=4170203&page=46")));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => { spendingChecks++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) => { spendingChecks++; return Task.CompletedTask; },
                CanRecoverOriginalWithoutGp = _ => true
            }));
        StringAssert.Contains(error.Message, "blank image placeholder");
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(0, spendingChecks);
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
    }

    [TestMethod]
    public async Task AnUnlistedOriginalReachedByRedirectCannotRestartAsAnOrdinaryNodeRecovery()
    {
        var preflight = 0;
        var final = 0;
        var freeChecks = 0;
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/")) return Html(Page());
            if (request.Uri.Host == "first.hath.network")
                return Redirect("https://exhentai.org/fullimg.php?private=signed-original-secret");
            if (request.Uri.Host == "exhentai.org") return Redirect(SecondImageUrl);
            return Html("private-html-body-secret");
        });
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl,
            new ExHentaiImageDownloadOptions
            {
                PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
                BeforeOriginalDownload = (_, _) => { preflight++; return Task.CompletedTask; },
                BeforeOriginalSend = (_, _) => { final++; return Task.CompletedTask; },
                CanRecoverOriginalWithoutGp = _ => { freeChecks++; return true; }
            }));
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.LocalPath.StartsWith("/s/")));
        Assert.AreEqual(1, handler.SentRequests.Count(r => r.Uri.LocalPath == "/fullimg.php"));
        Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.Host == "first.hath.network"));
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.Query.Contains("nl=")));
        Assert.AreEqual(1, preflight);
        Assert.AreEqual(1, final);
        Assert.AreEqual(0, freeChecks);
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        AssertSafeDiagnostic(error, "second.hath.network", "200", "text/html");
    }

    [TestMethod]
    public async Task RejectedTlsAuthenticationIsStillPermanentAfterDiagnosticSanitizing()
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? Html(Page()) :
            throw new HttpRequestException(HttpRequestError.SecureConnectionError, "secret " + ImageUrl,
                new AuthenticationException("private-auth-error-secret")));
        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => Client(handler).DownloadImage(PageUrl,
            OriginalOptions(() => { }, () => { })));
        Assert.IsFalse(TransientNetworkError.IsTransient(error));
        Assert.IsFalse(error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task OrdinaryImagesCannotRedirectIntoThePaidOriginalEntry()
    {
        var handler = new Handler(request => request.Uri.LocalPath.StartsWith("/s/") ? Html(Page()) :
            Redirect("https://exhentai.org/fullimg.php?private=signed-original-secret"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(handler).DownloadImage(PageUrl));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.LocalPath.StartsWith("/fullimg")));
    }

    [TestMethod]
    public async Task DirectImageFailuresReportResponseMetadataWithoutSignedPathsAndHaveNoReload()
    {
        var handler = new Handler(_ => Html("private-html-body-secret"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImageByUrl(ImageUrl));
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(error.Message, "page=direct image");
        StringAssert.Contains(error.Message, "finalHost=first.hath.network");
        StringAssert.Contains(error.Message, "status=200");
        StringAssert.Contains(error.Message, "contentType=text/html");
        StringAssert.Contains(error.Message, "format=unknown");
        Assert.IsFalse(error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
    }

    [TestMethod]
    public async Task AnHtmlContentTypeIsRejectedEvenWhenBytesHaveAnImageSignature()
    {
        var handler = new Handler(_ => Image(EncodedImage("PNG"), "text/html"));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImageByUrl(ImageUrl));
        StringAssert.Contains(error.Message, "format=PNG");
        StringAssert.Contains(error.Message, "contentType=text/html");
    }

    [TestMethod]
    public async Task CallerCancellationStopsBeforeANodeReload()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/")) return Html(Page());
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        try
        {
            await Client(handler).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions(), cancellation.Token);
            Assert.Fail("A cancelled image request must not succeed.");
        }
        catch (OperationCanceledException) { Assert.IsTrue(cancellation.IsCancellationRequested); }
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task BodyReadsRemainBoundedAfterResponseHeadersHaveArrived()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {Content = new WaitingContent()});
        var http = new HttpClient(handler) {Timeout = TimeSpan.FromMilliseconds(50)};
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var error = await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => Client(http).DownloadImageByUrl(ImageUrl, caller.Token));
        Assert.IsFalse(caller.IsCancellationRequested);
        Assert.IsTrue(TransientNetworkError.IsTransient(error));
        Assert.IsInstanceOfType<TimeoutException>(error.InnerException);
        Assert.IsFalse(error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(error.Message, "image body request timed out");
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("headerTimeout")]
    [DataRow("bodyTimeout")]
    [DataRow("503")]
    public async Task AReloadPageFailureDoesNotRepeatTheQuotaConsumingNlRequest(string failure)
    {
        var reloadRequests = 0;
        var logs = new Logs();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var handler = new Handler(request =>
        {
            if (request.Uri.LocalPath.StartsWith("/s/"))
            {
                if (request.Uri.Query.Contains("nl="))
                {
                    reloadRequests++;
                    return FailedResponse(failure);
                }
                return Html(Page());
            }
            return Html("private-html-body-secret");
        });
        using var http = new HttpClient(handler) {Timeout = TimeSpan.FromMilliseconds(50)};
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? error = null;
        try { await Client(http, loggerFactory).DownloadImage(PageUrl, new ExHentaiImageDownloadOptions(), caller.Token); }
        catch (Exception failureError) { error = failureError; }
        Assert.IsNotNull(error);
        Assert.IsFalse(caller.IsCancellationRequested);
        Assert.IsInstanceOfType(error, failure == "503" ? typeof(HttpRequestException) : typeof(TaskCanceledException));
        Assert.IsTrue(TransientNetworkError.IsTransient(error));
        Assert.IsTrue(ExHentaiClient.IsImageNodeRecoveryExhausted(error));
        Assert.AreEqual(1, reloadRequests);
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.IsFalse(logs.Messages.Any(m => m.Contains("retrying")));
        Assert.IsTrue(handler.Requests.Where(r => r.Uri.LocalPath.StartsWith("/s/")).All(r => r.LogKey == PageUrl));
        Assert.IsFalse(string.Join(" ", logs.Messages).Contains(ReloadToken));
        Assert.IsFalse(error.ToString().Contains(ReloadToken));
    }

    [TestMethod]
    public async Task InitialPageRetriesRemainAvailableAndTheirWarningsHideSensitiveQueryValues()
    {
        var pageRequests = 0;
        var logs = new Logs();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var handler = new Handler(request =>
        {
            if (!request.Uri.LocalPath.StartsWith("/s/")) return Image(EncodedImage("PNG"));
            if (++pageRequests == 1)
                throw new HttpRequestException(HttpRequestError.ConnectionError, "failed " + request.Uri);
            return Html(Page());
        });
        await Client(new HttpClient(handler), loggerFactory).DownloadImage(PageUrl + "?private=" + ReloadToken);
        Assert.AreEqual(2, pageRequests);
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.IsTrue(logs.Messages.Any(m => m.Contains("retrying")));
        Assert.IsFalse(string.Join(" ", logs.Messages).Contains(ReloadToken));
        Assert.IsTrue(handler.Requests.Where(r => r.Uri.LocalPath.StartsWith("/s/")).All(r => r.LogKey == PageUrl));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnExternalImageFirstCannotSeedTheGalleryCookieContainerAndNeverReceivesCredentials(bool defaultHeaders)
    {
        using var preparation = new PreparationHandler(new ThirdPartyCookieContainer());
        var requests = new List<(Uri Uri, string? Cookie, bool Authorization, bool Referer)>();
        using var http = new HttpClient(new PreparedTransport(preparation, requests));
        if (defaultHeaders)
        {
            http.DefaultRequestHeaders.Add("Cookie", "default_cookie=synthetic-secret");
            http.DefaultRequestHeaders.Add("Authorization", "Bearer synthetic-secret");
            http.DefaultRequestHeaders.Add("Referer", "https://exhentai.org/fullimg.php?secret=synthetic");
        }
        var client = Client(http);
        await client.DownloadImageByUrl(ImageUrl);
        http.DefaultRequestHeaders.Clear();
        await client.ParseList("https://exhentai.org/", includeMetadata: false);

        Assert.AreEqual(2, requests.Count);
        var external = requests[0];
        Assert.AreEqual("first.hath.network", external.Uri.Host);
        Assert.IsNull(external.Cookie);
        Assert.IsFalse(external.Authorization);
        Assert.IsFalse(external.Referer);
        var account = requests[1];
        Assert.AreEqual("exhentai.org", account.Uri.Host);
        Assert.IsNotNull(account.Cookie);
        StringAssert.Contains(account.Cookie, "ipb_member_id=123");
        StringAssert.Contains(account.Cookie, "ipb_pass_hash=synthetic-cookie-secret");
    }

    private static ExHentaiImageDownloadOptions OriginalOptions(Action preflight, Action final) => new()
    {
        PreferOriginal = true, RequestContext = new ExHentaiRequestContext(Cookie),
        BeforeOriginalDownload = (_, _) => { preflight(); return Task.CompletedTask; },
        BeforeOriginalSend = (_, _) => { final(); return Task.CompletedTask; }
    };

    private static string Page(string image = ImageUrl, string? onclick = null, bool includeLoadFail = true,
        string? original = null) =>
        $"<div><img id='img' src='{WebUtility.HtmlEncode(image)}'" +
        (!includeLoadFail ? $" onerror=\"{WebUtility.HtmlEncode(onclick ?? "") }\"" : "") + "></div>" +
        (includeLoadFail ? $"<a id='loadfail' onclick=\"{WebUtility.HtmlEncode(onclick ?? "return nl('" + ReloadToken + "')") }\">Reload</a>" : "") +
        (original == null ? "" : $"<a href='{WebUtility.HtmlEncode(original)}'>Download original 1600 x 2200 3.08 MB source</a>");

    private static byte[] EncodedImage(string format)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(120, 30, 45);
        using var stream = new MemoryStream();
        IImageEncoder encoder = format switch
        {
            "PNG" => new PngEncoder(), "JPEG" => new JpegEncoder(), "GIF" => new GifEncoder(), "WebP" => new WebpEncoder(),
            _ => throw new ArgumentException("Unknown test format.", nameof(format))
        };
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static void AssertSafeDiagnostic(Exception error, string host, string status, string contentType)
    {
        StringAssert.Contains(error.Message, "page=" + PageUrl);
        StringAssert.Contains(error.Message, "pageNumber=46");
        StringAssert.Contains(error.Message, "finalHost=" + host);
        StringAssert.Contains(error.Message, "status=" + status);
        StringAssert.Contains(error.Message, "contentType=" + contentType);
        Assert.IsFalse(error.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(error.ToString().Contains(ReloadToken));
        Assert.IsFalse(error.ToString().Contains("?"));
    }

    private static long ExtractLength(string message) => long.Parse(
        System.Text.RegularExpressions.Regex.Match(message, @"\blength=(\d+)").Groups[1].Value);

    private static HttpResponseMessage FailedResponse(string failure) => failure switch
    {
        "html" => Html("node unavailable"),
        "empty" => Image([]),
        "invalid" => Image("private-binary-payload-secret"u8.ToArray()),
        "403" => new HttpResponseMessage(HttpStatusCode.Forbidden) {Content = new StringContent("node unavailable")},
        "404" => new HttpResponseMessage(HttpStatusCode.NotFound) {Content = new StringContent("node unavailable")},
        "503" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {Content = new StringContent("node unavailable")},
        "transport" => throw new HttpRequestException(HttpRequestError.ConnectionError, "failed " + ImageUrl),
        "bodyTransport" => new HttpResponseMessage(HttpStatusCode.OK) {Content = new BrokenContent()},
        "headerTimeout" => throw new TaskCanceledException("private-timeout-secret " + ImageUrl,
            new TimeoutException("private-inner-secret " + ImageUrl)),
        "bodyTimeout" => new HttpResponseMessage(HttpStatusCode.OK) {Content = new WaitingContent()},
        _ => throw new ArgumentException("Unknown test failure.", nameof(failure))
    };

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
        {Content = new StringContent(html, Encoding.UTF8, "text/html")};

    private static HttpResponseMessage DatedHtml(string html)
    {
        var response = Html(html);
        response.Headers.Date = DateTimeOffset.UtcNow;
        return response;
    }

    private static HttpResponseMessage Image(byte[] bytes, string mediaType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(bytes)};
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private static HttpResponseMessage Redirect(string url)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(url, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static ExHentaiClient Client(Handler handler) => Client(new HttpClient(handler));
    private static ExHentaiClient Client(HttpClient http, ILoggerFactory? loggerFactory = null) =>
        new(new Factory(http), loggerFactory ?? NullLoggerFactory.Instance);
    private sealed class Factory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    private sealed record Request(Uri Uri, string? Cookie, string? CookieOverride, string? AccountKey,
        string? LogKey, bool SkipConfiguredHeaders, bool SuppressSensitiveHeaders);
    private sealed class Handler(Func<Request, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public List<Request> SentRequests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            request.Options.TryGetValue(ThirdPartyRequestOptions.Cookie, out var cookie);
            request.Options.TryGetValue(ThirdPartyRequestOptions.AccountKey, out var account);
            request.Options.TryGetValue(ThirdPartyRequestOptions.RequestLogKey, out var logKey);
            request.Options.TryGetValue(ThirdPartyRequestOptions.SkipConfiguredHeaders, out var skip);
            request.Options.TryGetValue(ThirdPartyRequestOptions.SuppressSensitiveHeaders, out var suppress);
            var record = new Request(request.RequestUri!, request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values) : null, cookie, account, logKey, skip, suppress);
            Requests.Add(record);
            if (request.Options.TryGetValue(ThirdPartyRequestOptions.BeforeSend, out var before)) await before(ct);
            SentRequests.Add(record);
            return respond(record);
        }
    }

    private sealed class BrokenContent : HttpContent
    {
        public BrokenContent() => Headers.ContentType = new MediaTypeHeaderValue("image/png");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new HttpIOException(HttpRequestError.ResponseEnded, "private-wire-error-secret " + ImageUrl));
        protected override bool TryComputeLength(out long length) { length = 64; return true; }
    }

    private sealed class WaitingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The cancellable body-read overload is required.");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct) =>
            Task.Delay(Timeout.Infinite, ct);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class PreparationHandler(IThirdPartyCookieContainer cookies)
        : AbstractThirdPartyHttpMessageHandler<PreparationOptions>(
            new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance), ThirdPartyId.ExHentai,
            new BakabaseWebProxy(new NetworkOptionsProvider()), new PreparationOptions(), cookies)
    {
        public Task PrepareAsync(HttpRequestMessage request, CancellationToken ct) => base.BeforeRequestingAsync(request, ct);
    }

    private sealed class PreparedTransport(PreparationHandler preparation,
        List<(Uri Uri, string? Cookie, bool Authorization, bool Referer)> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await preparation.PrepareAsync(request, ct);
            requests.Add((request.RequestUri!, request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("; ", values) : null, request.Headers.Contains("Authorization"), request.Headers.Contains("Referer")));
            return request.RequestUri!.Host.EndsWith("hath.network") ? Image(EncodedImage("PNG")) : Html("<div class='itg'></div>");
        }
    }

    private sealed class PreparationOptions : IThirdPartyHttpClientOptions
    {
        public int MaxConcurrency => 1;
        public int RequestInterval => 0;
        public string? Cookie => ExHentaiImageRecoveryTests.Cookie;
        public string? UserAgent => null;
        public string? Referer => "https://exhentai.org/fullimg.php?secret=synthetic";
        public Dictionary<string, string>? Headers => new()
        {
            ["Cookie"] = "configured_cookie=synthetic-secret", ["Authorization"] = "Bearer configured-secret"
        };
    }

    private sealed class NetworkOptionsProvider : IBOptions<NetworkOptions>
    {
        public NetworkOptions Value { get; } = new();
    }

    private sealed class Logs : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, error) + error);
    }
}
