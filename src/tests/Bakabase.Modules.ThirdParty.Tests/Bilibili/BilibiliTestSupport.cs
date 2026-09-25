using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Bakabase.Modules.ThirdParty.Components.Localization;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

/// <summary>Loads the scrubbed fixtures in <c>Fixtures/Bilibili</c> (copied next to the test assembly).</summary>
internal static class BilibiliFixtures
{
    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bilibili");

    public static string Read(string name) => File.ReadAllText(Path.Combine(DirectoryPath, name));

    public static DataWrapper<T> Wrapper<T>(string name) =>
        JsonConvert.DeserializeObject<DataWrapper<T>>(Read(name), new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
        })!;

    public static T Data<T>(string name) where T : class => Wrapper<T>(name).Data!;

    public static T Load<T>(string name) => JsonConvert.DeserializeObject<T>(Read(name))!;
}

/// <summary>Asserts that nothing signed, secret or body-like leaks through an exception.</summary>
internal static class BilibiliSecrets
{
    public const string CookieSentinel = "SENTINEL_COOKIE_VALUE";

    private static readonly string[] Secrets = ["upsig=", "deadline=", "oi=", "auth_key=", "SESSDATA", CookieSentinel];

    /// <param name="allowRedactedUrls">Whether URLs reduced by <c>BilibiliCdnUrls.Redact</c> may appear (they
    /// may, per the redaction rule); by default no URL at all may appear.</param>
    public static void AssertClean(Exception e, bool allowRedactedUrls = false)
    {
        string[] forbiddenList = allowRedactedUrls ? Secrets : [..Secrets, "https://", "http://"];
        foreach (var text in new[] {e.Message, e.ToString()})
        {
            foreach (var forbidden in forbiddenList)
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"{e.GetType().Name} leaks '{forbidden}': {text}");
            }
        }
    }

    public static void AssertClean(string text)
    {
        foreach (var forbidden in Secrets)
        {
            Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"'{forbidden}' leaked: {text}");
        }
    }
}

internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class StubThirdPartyLocalizer : IThirdPartyLocalizer
{
    public const string CookieInvalid = "The Bilibili cookie is invalid (localized).";
    public string ThirdParty_Bilibili_CookieIsInvalid() => CookieInvalid;
    public string ThirdParty_Bilibili_FavoritesIsMissing() => "missing";
}

/// <summary>Answers API requests by the first registered URL fragment the request URL contains.</summary>
internal sealed class FakeBilibiliApi : HttpMessageHandler
{
    private readonly List<(string Fragment, Func<HttpResponseMessage> Respond)> _routes = [];
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public FakeBilibiliApi Json(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK,
        string mediaType = "application/json")
    {
        _routes.Add((fragment, () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        }));
        return this;
    }

    public FakeBilibiliApi Fixture(string fragment, string fixture) => Json(fragment, BilibiliFixtures.Read(fixture));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Requests.Enqueue(request);
        var url = request.RequestUri!.ToString();
        foreach (var (fragment, respond) in _routes)
        {
            if (url.Contains(fragment, StringComparison.Ordinal))
            {
                return Task.FromResult(respond());
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no route"),
        });
    }
}

internal sealed class CapturingLoggerFactory : ILoggerFactory, ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private sealed class Logger(CapturingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}
