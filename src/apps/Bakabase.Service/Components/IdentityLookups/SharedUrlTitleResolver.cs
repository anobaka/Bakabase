using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.IdentityLookups;

/// <summary>
/// Reads what a shared page calls the thing it is sharing.
/// <para>
/// A sharing channel is not a source: nobody holds anything there and the page describes whatever
/// its author felt like writing. So this deliberately takes one thing — the title — and leaves
/// parsing the actual download links to the acquisition pipeline, which knows what it is looking
/// for.
/// </para>
/// </summary>
public class SharedUrlTitleResolver(
    IHttpClientFactory httpClientFactory,
    SoulPlusClient soulPlusClient,
    ILogger<SharedUrlTitleResolver> logger) : ISharedUrlTitleResolver
{
    private static readonly Regex TitleTag =
        new(@"<title[^>]*>(?<title>.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public async Task<string?> ResolveTitle(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // SoulPlus posts hide their subject from a plain <title> read behind the forum's own
        // markup, and the client already knows how to log in and find it.
        if (uri.Host.Contains("soul-plus", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var post = await soulPlusClient.GetPostAsync(url, ct);
                if (!string.IsNullOrWhiteSpace(post.Title))
                {
                    return post.Title.Trim();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[SharedUrl] Could not read the SoulPlus post at {Url}; falling back to its HTML title", url);
            }
        }

        var client = httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);
        var html = await client.GetStringAsync(url, ct);
        var match = TitleTag.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var title = HttpUtility.HtmlDecode(match.Groups["title"].Value).Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }
}
