using System;
using System.Collections.Generic;
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
/// One named ExHentai gallery.
/// <para>
/// A source that lists exactly one thing, which is a real if unusual case: "keep this one in the
/// collection and let me see whether I have it". It used to watch the gallery for changes — pages
/// added, tags edited — and that is gone: a source now says what it lists, and a collection's
/// difference is measured against its members, so there is nowhere for "the same item, but
/// different" to live. Watching one gallery for edits would need its own mechanism.
/// </para>
/// </summary>
public class ExHentaiGalleryProvider : ISubscriptionProvider
{
    private readonly ExHentaiClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ExHentaiGalleryProvider(ExHentaiClient client)
    {
        _client = client;
    }

    public string Kind => "exhentai.gallery";
    public string DisplayName => "ExHentai Gallery";
    public string? Icon => null;
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.PlatformHolding;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.ExHentai;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        if (target is null || string.IsNullOrWhiteSpace(target.Url))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("Gallery URL is required"));
        }

        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("URL must be an absolute http(s) URL"));
        }

        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson) => TryParse(targetJson)?.Url ?? "";

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");

        var detail = await _client.ParseDetail(target.Url, includeTorrents: false);

        return
        [
            new SubscriptionItem(
                detail.Id.ToString(),
                string.IsNullOrEmpty(detail.Name) ? detail.RawName : detail.Name,
                detail.Url,
                detail.CoverUrl == null ? null : [detail.CoverUrl])
        ];
    }

    private static ExHentaiGalleryTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ExHentaiGalleryTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
