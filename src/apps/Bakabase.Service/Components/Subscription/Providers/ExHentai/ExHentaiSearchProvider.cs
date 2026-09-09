using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

namespace Bakabase.Service.Components.Subscription.Providers.ExHentai;

/// <summary>
/// An ExHentai/E-hentai list URL — a saved search, the Watched feed, a favourites page. What it
/// lists is what the user has access to on the platform, so each gallery carries its ExHentai
/// identity and can be fetched through the existing downloader.
/// </summary>
public class ExHentaiSearchProvider : ISubscriptionProvider
{
    private readonly ExHentaiClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ExHentaiSearchProvider(ExHentaiClient client)
    {
        _client = client;
    }

    public string Kind => "exhentai.search";
    public string DisplayName => "ExHentai Search";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.PlatformHolding;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.ExHentai;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);
        if (target is null || string.IsNullOrWhiteSpace(target.Url))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("URL is required"));
        }
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("URL must be an absolute http(s) URL"));
        }
        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson) =>
        TryParse(targetJson)?.Url ?? "";

    /// <summary>
    /// Every page of the list, followed to the end. A search that stopped at page one would keep
    /// finding the same twenty-five galleries and never the rest.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");

        var items = new List<SubscriptionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var url = target.Url;

        // A list URL that never stops paging would fetch a whole site; the cap is what keeps a
        // mis-typed target from becoming an afternoon of requests.
        for (var page = 0; page < MaxPages && !string.IsNullOrEmpty(url); page++)
        {
            ct.ThrowIfCancellationRequested();

            var list = await _client.ParseList(url);

            foreach (var r in list.Resources)
            {
                if (seen.Add(r.Id.ToString()))
                {
                    items.Add(new SubscriptionItem(r.Id.ToString(), r.Name, r.Url,
                        r.CoverUrl == null ? null : [r.CoverUrl]));
                }
            }

            // The same URL again means the site stopped offering a next page.
            url = list.NextListUrl == url ? null : list.NextListUrl;
        }

        return items;
    }

    /// <summary>How far a single check will page. Twenty-five a page, so a thousand galleries.</summary>
    private const int MaxPages = 40;

    private static ExHentaiSearchTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ExHentaiSearchTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
