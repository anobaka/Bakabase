using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Cookie validation is a live request to the source, and it runs as the first act of every start
/// attempt. Draining a queue of a thousand tasks therefore made a thousand identical requests before
/// downloading anything — and gave a transient network blip a thousand chances to park a task in
/// Failed. A proven-good cookie is now taken on trust for a few minutes.
/// </summary>
[TestClass]
public class DownloaderCookieValidationCacheTests
{
    private sealed class TestOptions : ISimpleDownloaderOptionsHolder
    {
        public string? Cookie { get; set; } = "session=abc";
        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; }
        public string? DefaultPath { get; set; } = "/downloads";
        public string? NamingConvention { get; set; }
        public bool SkipExisting { get; set; }
        public int MaxRetries { get; set; }
        public int RequestTimeout { get; set; }
    }

    private sealed class StubOptionsManager(TestOptions value) : IBOptionsManager<TestOptions>
    {
        public TestOptions Value { get; } = value;

        public void Save(TestOptions options)
        {
        }

        public Task SaveAsync(TestOptions options) => Task.CompletedTask;

        public Task SaveAsync(Action<TestOptions> modify)
        {
            modify(Value);
            return Task.CompletedTask;
        }
    }

    private sealed class StubLocalizer : IDownloaderLocalizer
    {
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public string GetDownloaderName<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType) => "";
        public string? GetDownloaderDescription<TEnum>(ThirdPartyId thirdPartyId, TEnum taskType) => null;
        public string GetNamingFieldName<TEnum>(TEnum namingFieldValue) => "";
        public string? GetNamingFieldDescription<TEnum>(TEnum namingFieldValue) => null;
        public string? GetNamingFieldExample<TEnum>(TEnum namingFieldValue) => null;
        public string InvalidFavorites() => "";
        public string FfMpegIsNotReady() => "";
        public string InvalidCookie() => "invalid cookie";
        public string DownloadPathNotSet() => "";
        public string TransientNetworkErrorRetrying(int delaySeconds, int retry, int maxRetries) => "";

        public string DownloadNoticesSummary(int count) => $"{count} notices";
        public string DownloadNoticesTruncated(int remaining) => $"{remaining} more";
        public string BilibiliFavoritesNotFound(string favoritesId, string? name) => "";
        public string BilibiliRiskControl(int? code) => "";
        public string BilibiliRiskControlWaiting(int minutes, int retry, int maxRetries) => "";
        public string BilibiliNotLoggedIn() => "";
        public string BilibiliDiskFull(string path) => "";

        public string DescribeBilibiliSkip(BilibiliSkipReason reason, int? code, string? message) =>
            reason.ToString();

        public string BilibiliSkipNotice(string subject, string reason) => "";
        public string BilibiliSkipFooter() => "";
    }

    private sealed class TestHelper(StubOptionsManager optionsManager, bool cookieIsValid)
        : AbstractDownloaderHelper<TestOptions>(optionsManager, new StubLocalizer(), new HttpClient())
    {
        public int Validations;

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        protected override string? CookieValidationUrl => "https://example.invalid/";

        protected override Task<bool> ValidateCookieAsync(string cookie)
        {
            Validations++;
            return Task.FromResult(cookieIsValid);
        }
    }

    /// <summary>Validates for real, over a scripted transport.</summary>
    private sealed class LiveHelper(
        StubOptionsManager optionsManager,
        ScriptedTransport transport,
        TimeSpan? fastFailure = null)
        : AbstractDownloaderHelper<TestOptions>(optionsManager, new StubLocalizer(), new HttpClient(transport))
    {
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        protected override string? CookieValidationUrl => "https://example.invalid/";

        protected override TimeSpan CookieValidationFastFailure => fastFailure ?? base.CookieValidationFastFailure;
    }

    private sealed class ScriptedTransport(params Func<HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        public int Requests;
        public TimeSpan Latency = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var reply = replies[Math.Min(Requests, replies.Length - 1)];
            Requests++;
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, ct);
            }

            return reply();
        }
    }

    private static HttpResponseMessage DroppedConnection() => throw new HttpRequestException(
        HttpRequestError.SecureConnectionError, "The SSL connection could not be established, see inner exception.",
        new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));

    private static HttpResponseMessage Reply(HttpStatusCode status) => new(status);

    [TestMethod]
    public async Task ANetworkBlip_IsRetriedInsteadOfReportedAsAnInvalidCookie()
    {
        var transport = new ScriptedTransport(DroppedConnection, () => Reply(HttpStatusCode.OK));
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport);

        Assert.AreEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(2, transport.Requests);
    }

    [TestMethod]
    public async Task AServerThatIsBrieflyUnavailable_IsAskedAgain()
    {
        var transport = new ScriptedTransport(() => Reply(HttpStatusCode.ServiceUnavailable),
            () => Reply(HttpStatusCode.OK));
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport);

        Assert.AreEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(2, transport.Requests);
    }

    [TestMethod]
    public async Task APersistentOutage_StillFailsAfterAFewAttempts()
    {
        var transport = new ScriptedTransport(DroppedConnection);
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport);

        Assert.AreNotEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(3, transport.Requests);
    }

    [TestMethod]
    public async Task ARejectedCookie_IsNotAskedAgain()
    {
        var transport = new ScriptedTransport(() => Reply(HttpStatusCode.Forbidden));
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport);

        Assert.AreNotEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(1, transport.Requests);
    }

    [TestMethod]
    public async Task ASlowFailure_IsNotRepeated()
    {
        // An unreachable host fails only once the OS connect timeout runs out (tens of seconds), and
        // this check runs inside the queue's scheduling pass.
        var transport = new ScriptedTransport(DroppedConnection) { Latency = TimeSpan.FromMilliseconds(300) };
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport,
            fastFailure: TimeSpan.FromMilliseconds(100));

        Assert.AreNotEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(1, transport.Requests);
    }

    [TestMethod]
    public async Task ATimeout_IsNotRepeated()
    {
        // The client has already waited out its whole timeout, inside the queue's scheduling pass.
        var transport = new ScriptedTransport(() => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()));
        var helper = new LiveHelper(new StubOptionsManager(new TestOptions()), transport);

        Assert.AreNotEqual(0, (await helper.ValidateOptionsAsync()).Code);
        Assert.AreEqual(1, transport.Requests);
    }

    [TestMethod]
    public async Task AProvenCookie_IsNotRevalidatedOnEveryStart()
    {
        var options = new TestOptions();
        var helper = new TestHelper(new StubOptionsManager(options), cookieIsValid: true);

        for (var i = 0; i < 50; i++)
        {
            Assert.IsTrue((await helper.ValidateOptionsAsync()).Code == 0);
        }

        Assert.AreEqual(1, helper.Validations,
            "A queue of fifty tasks must cost one cookie check, not fifty round trips to the source.");
    }

    [TestMethod]
    public async Task ChangingTheCookie_ForcesAFreshCheck()
    {
        var options = new TestOptions();
        var helper = new TestHelper(new StubOptionsManager(options), cookieIsValid: true);

        await helper.ValidateOptionsAsync();
        options.Cookie = "session=def";
        await helper.ValidateOptionsAsync();

        Assert.AreEqual(2, helper.Validations);
    }

    [TestMethod]
    public async Task AFailingCookie_IsRetriedRatherThanRemembered()
    {
        // Only success is cached: a user who has just pasted a working cookie must not have to wait
        // out a window before the queue believes them.
        var helper = new TestHelper(new StubOptionsManager(new TestOptions()), cookieIsValid: false);

        await helper.ValidateOptionsAsync();
        await helper.ValidateOptionsAsync();

        Assert.AreEqual(2, helper.Validations);
    }

    [TestMethod]
    public async Task NoCookieConfigured_NeverValidates()
    {
        var helper = new TestHelper(new StubOptionsManager(new TestOptions { Cookie = null }),
            cookieIsValid: true);

        await helper.ValidateOptionsAsync();

        Assert.AreEqual(0, helper.Validations);
    }
}
