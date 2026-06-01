using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

namespace Bakabase.Service.Components.Subscription.Providers.ExHentai;

/// <summary>
/// Watches an ExHentai/E-hentai list URL (saved search, Watched feed, etc.) and
/// emits a notification when new galleries appear.
/// </summary>
public class ExHentaiSearchProvider : AbstractListIdDiffProvider
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

    public override string Kind => "exhentai.search";
    public override string DisplayName => "ExHentai Search";

    public override Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
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

    public override string DescribeTarget(string targetJson) =>
        TryParse(targetJson)?.Url ?? "";

    protected override async Task<IReadOnlyList<SubscriptionItem>> FetchCurrentItemsAsync(
        SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
            ?? throw new InvalidOperationException("Invalid target payload");

        var list = await _client.ParseList(target.Url);
        return list.Resources.Select(r => new SubscriptionItem(
            Id: r.Id.ToString(),
            Title: r.Name,
            Url: r.Url,
            ThumbnailUrl: r.CoverUrl)).ToList();
    }

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
