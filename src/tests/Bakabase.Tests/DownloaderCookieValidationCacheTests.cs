using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Models.Constants;
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
        public string LuxIsNotReady() => "";
        public string InvalidCookie() => "invalid cookie";
        public string DownloadPathNotSet() => "";
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
