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
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;

namespace Bakabase.Service.Components.Subscription.Providers.Pixiv;

/// <summary>
/// The user's Pixiv "follow latest" feed — illustrations posted by artists they follow. What it
/// lists is available to them on the platform, so each illustration carries its Pixiv identity and
/// can be fetched through the existing downloader.
/// </summary>
public class PixivFollowLatestProvider : ISubscriptionProvider
{
    private readonly PixivClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private const string AllowedModeAll = "all";
    private const string AllowedModeR18 = "r18";

    public PixivFollowLatestProvider(PixivClient client)
    {
        _client = client;
    }

    public string Kind => "pixiv.followLatest";
    public string DisplayName => "Pixiv Follow Feed";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.PlatformHolding;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Pixiv;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson) ?? new PixivFollowLatestTarget();
        if (target.Mode != AllowedModeAll && target.Mode != AllowedModeR18)
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("Mode must be 'all' or 'r18'"));
        }
        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson) ?? new PixivFollowLatestTarget();
        return target.Mode == AllowedModeR18 ? "R-18" : "All";
    }

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson) ?? new PixivFollowLatestTarget();
        var url = $"https://www.pixiv.net/ajax/follow_latest/illust?p=1&mode={Uri.EscapeDataString(target.Mode)}";
        var response = await _client.FollowLatestIllust(url);

        var thumbnails = response?.Thumbnails?.Illust;
        if (thumbnails is null) return [];

        return thumbnails
            .Where(i => !string.IsNullOrEmpty(i.IllustId))
            .Select(i => new SubscriptionItem(
                i.IllustId,
                i.IllustTitle ?? i.Title ?? i.IllustId,
                $"https://www.pixiv.net/artworks/{i.IllustId}",
                i.Urls?.Thumb == null ? null : [i.Urls.Thumb]))
            .ToList();
    }

    private static PixivFollowLatestTarget? TryParse(string targetJson)
    {
        if (string.IsNullOrEmpty(targetJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<PixivFollowLatestTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
