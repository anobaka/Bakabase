using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using CsQuery;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Helpers;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using HttpCloak;
using static Bakabase.Abstractions.Components.Configuration.InternalOptions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusClient(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IBOptions<ISoulPlusOptions> options,
    SoulPlusRequestGate? requestGate = null)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    private readonly SoulPlusRequestGate _requestGate = requestGate ?? new(options);
    public async Task<SoulPlusPost> GetPostAsync(string link, CancellationToken ct)
    {
        var firstPage = SoulPlusPostParser.FirstPageUrl(link);
        return SoulPlusPostParser.Parse(await GetHtml(firstPage, ct), firstPage);
    }

    public string PurchaseAccountKey => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(options.Value.Cookie ?? "")));

    protected async Task<string> GetHtml(string url, CancellationToken ct)
    {
        if (!SoulPlusPostParser.IsSupportedUrl(url))
            throw new InvalidOperationException("This URL is not a supported SoulPlus site.");
        // Old shared links may use HTTP; account cookies are only sent over HTTPS.
        url = new UriBuilder(url) {Scheme = Uri.UriSchemeHttps, Port = -1}.Uri.AbsoluteUri;
        if (options.Value.Cookie.IsNullOrEmpty())
        {
            throw new Exception("Cookie is not set");
        }

        ct.ThrowIfCancellationRequested();

        var preset = options.Value.TlsPreset ?? TlsPresetHelper.DefaultPreset;
        var userAgent = options.Value.UserAgent ?? IThirdPartyHttpClientOptions.DefaultUserAgent;

        var headers = new Dictionary<string, string>
        {
            { "User-Agent", userAgent },
            { "Cookie", options.Value.Cookie }
        };

        var response = await _requestGate.ExecuteAsync(() => SendAsync(url, headers, preset), ct);
        if (response.StatusCode != 200)
        {
            throw new Exception(
                $"Request to {url} failed with status code: {response.StatusCode}");
        }

        return response.Text;
    }

    protected virtual async Task<(int StatusCode, string Text)> SendAsync(string url,
        Dictionary<string, string> headers, string preset)
    {
        using var session = new Session(preset: preset, timeout: 30, retry: 0);
        // HttpCloak 1.6.1 cancels its public Task before its native callback has drained.
        // Wait for real completion (bounded by the session timeout) before releasing the site slot;
        // queued requests remain immediately cancellable and no purchase is retried.
        var response = await session.GetAsync(url, headers: headers, cancellationToken: CancellationToken.None);
        return (response.StatusCode, response.Text);
    }

    public async Task BuyLockedContent(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.AbsolutePath.EndsWith("job.php", StringComparison.OrdinalIgnoreCase) ||
            !uri.Query.Contains("buy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The locked item has no recognized purchase endpoint.");
        await GetHtml(url, ct);
    }

    /// <summary>
    /// The threads on a board, following the pager to the end.
    /// </summary>
    /// <param name="listUrl">A board's list page, as the user would paste it.</param>
    /// <param name="maxPages">
    /// How far to follow. A board with ten years of history is not something to walk on every
    /// check, and the newest pages are where new shares are.
    /// </param>
    public async Task<List<SoulPlusThread>> GetThreadsAsync(string listUrl, int maxPages,
        CancellationToken ct)
    {
        var threads = new List<SoulPlusThread>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var url = listUrl;

        for (var page = 0; page < maxPages && !string.IsNullOrEmpty(url); page++)
        {
            ct.ThrowIfCancellationRequested();

            var html = await GetHtml(url, ct);

            foreach (var thread in SoulPlusListParser.Parse(html, url))
            {
                if (seen.Add(thread.Tid)) threads.Add(thread);
            }

            url = SoulPlusListParser.FindNextPageUrl(html, url);
        }

        return threads;
    }

    protected override string HttpClientName => HttpClientNames.SoulPlus;
}
