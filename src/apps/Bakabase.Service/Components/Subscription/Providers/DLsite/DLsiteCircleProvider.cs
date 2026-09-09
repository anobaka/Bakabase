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
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;

namespace Bakabase.Service.Components.Subscription.Providers.DLsite;

/// <summary>What a DLsite circle subscription watches.</summary>
public record DLsiteCircleTarget
{
    /// <summary>A circle profile or series page, as the user would paste it.</summary>
    public string Url { get; set; } = "";

    /// <summary>How many listing pages to walk per check.</summary>
    public int MaxPages { get; set; } = 1;
}

/// <summary>
/// A circle's works, or a series' entries.
/// <para>
/// A catalog: it says what exists, not what you have and not where to get it. Every work it lists
/// becomes a resource carrying its DLsite identity, so buying one later recognises it rather than
/// making a second copy — and a work you have never heard of shows up as missing, which is the
/// point of watching a circle at all.
/// </para>
/// </summary>
public class DLsiteCircleProvider : ISubscriptionProvider
{
    private readonly DLsiteClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private const int MaxPagesEver = 20;

    public DLsiteCircleProvider(DLsiteClient client)
    {
        _client = client;
    }

    public string Kind => "dlsite.circle";
    public string DisplayName => "DLsite Circle / Series";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.Catalog;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.DLsite;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        if (target is null || string.IsNullOrWhiteSpace(target.Url))
        {
            return Task.FromResult(
                SubscriptionValidationResult.Invalid("A circle or series page URL is required"));
        }

        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(
                SubscriptionValidationResult.Invalid("URL must be an absolute http(s) URL"));
        }

        if (!uri.Host.Contains("dlsite.com", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("That is not a DLsite page"));
        }

        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson) => TryParse(targetJson)?.Url ?? "";

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");

        var works = await _client.GetListedWorksAsync(target.Url,
            Math.Clamp(target.MaxPages, 1, MaxPagesEver), ct);

        return works
            .Select(w => new SubscriptionItem(
                w.WorkId,
                w.Title,
                // The work's own page, for a user who wants to look at it. It is not a lead: a
                // catalog says what exists, not how to get it.
                $"https://www.dlsite.com/{DLsiteClient.GetCategoryByWorkId(w.WorkId)}/work/=/product_id/{w.WorkId}.html",
                w.CoverUrl == null ? null : [w.CoverUrl]))
            .ToList();
    }

    private static DLsiteCircleTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<DLsiteCircleTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
