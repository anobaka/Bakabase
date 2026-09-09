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
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

namespace Bakabase.Service.Components.Subscription.Providers.SoulPlus;

/// <summary>
/// A SoulPlus board, optionally narrowed by what a thread's title says.
/// <para>
/// The first sharing channel: what it lists are posts, not works. Nobody holds these on a
/// platform and no authority says they exist — somebody shared a link, which is why an item here
/// becomes an acquisition lead rather than an identity. Two posts about the same game are one
/// resource with two ways of getting it.
/// </para>
/// </summary>
public class SoulPlusSearchProvider : ISubscriptionProvider
{
    private readonly SoulPlusClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>A board with years of history is not something to walk on every check.</summary>
    private const int MaxPagesEver = 20;

    public SoulPlusSearchProvider(SoulPlusClient client)
    {
        _client = client;
    }

    public string Kind => "soulplus.search";
    public string DisplayName => "SoulPlus Board";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.SharingChannel;

    /// <summary>
    /// None. A forum post is an act of sharing; treating a thread id as an identity would claim
    /// the post is the work, and the next post about the same game would be a second copy of it.
    /// </summary>
    public ResourceSource? ResourceSource => null;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        if (target is null || string.IsNullOrWhiteSpace(target.Url))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("A board URL is required"));
        }

        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(
                SubscriptionValidationResult.Invalid("URL must be an absolute http(s) URL"));
        }

        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson);

        if (target is null) return "";

        var keywords = target.Keywords?.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();

        return keywords is {Count: > 0} ? $"{target.Url} · {string.Join(", ", keywords)}" : target.Url;
    }

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");

        var threads = await _client.GetThreadsAsync(target.Url,
            Math.Clamp(target.MaxPages, 1, MaxPagesEver), ct);

        var keywords = target.Keywords?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToList();

        return threads
            .Where(thread => keywords is not {Count: > 0} ||
                             keywords.Any(k => thread.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            // The thread's own link is the item's url, which is what becomes the lead. The tid
            // only separates one post from another within the channel.
            .Select(thread => new SubscriptionItem(thread.Tid, thread.Title, thread.Url))
            .ToList();
    }

    private static SoulPlusSearchTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<SoulPlusSearchTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
