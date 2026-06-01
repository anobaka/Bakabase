using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;

namespace Bakabase.Service.Components.Subscription.Providers.Pixiv;

/// <summary>
/// Watches the user's Pixiv "follow latest" feed — illustrations posted by users
/// they follow. Inherits the framework's id-based diff: new illustrations relative
/// to the previous snapshot become notifications.
/// </summary>
public class PixivFollowLatestProvider : AbstractListIdDiffProvider
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

    public override string Kind => "pixiv.followLatest";
    public override string DisplayName => "Pixiv Follow Feed";

    public override Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson) ?? new PixivFollowLatestTarget();
        if (target.Mode != AllowedModeAll && target.Mode != AllowedModeR18)
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("Mode must be 'all' or 'r18'"));
        }
        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public override string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson) ?? new PixivFollowLatestTarget();
        return target.Mode == AllowedModeR18 ? "R-18" : "All";
    }

    protected override async Task<IReadOnlyList<SubscriptionItem>> FetchCurrentItemsAsync(
        SubscriptionRecord subscription,
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
                Id: i.IllustId,
                Title: i.IllustTitle ?? i.Title ?? i.IllustId,
                Url: $"https://www.pixiv.net/artworks/{i.IllustId}",
                ThumbnailUrl: i.Urls?.Thumb))
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
