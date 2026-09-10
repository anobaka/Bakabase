using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Vndb;

namespace Bakabase.Service.Components.Subscription.Providers.Vndb;

/// <summary>What a VNDB developer subscription watches.</summary>
public record VndbDeveloperTarget
{
    /// <summary>A producer id (<c>p17</c>), or the page URL it came from — both are accepted.</summary>
    public string Producer { get; set; } = "";
}

/// <summary>
/// Everything a developer has made, as VNDB has it.
/// <para>
/// A catalog source: VNDB says a work exists and what it is called, and nothing about where to get
/// it. That is exactly why its members show as missing until a lead turns up — which, for following
/// a studio you like, is the useful thing to be told.
/// </para>
/// </summary>
public partial class VndbDeveloperProvider(VndbClient client) : ISubscriptionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Kind => "vndb.developer";
    public string DisplayName => "VNDB Developer";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.Catalog;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Vndb;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        return Task.FromResult(target is null || ProducerIdOf(target.Producer) is null
            ? SubscriptionValidationResult.Invalid("A VNDB producer id (p17) or page URL is required")
            : SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson);

        return target is null ? "" : $"vndb {ProducerIdOf(target.Producer) ?? target.Producer}";
    }

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");
        var producerId = ProducerIdOf(target.Producer)
                         ?? throw new InvalidOperationException("Invalid producer");

        return (await client.GetByDeveloperAsync(producerId, ct))
            .Select(vn => new SubscriptionItem(
                vn.Id,
                vn.DisplayName,
                vn.Url,
                vn.Image?.Url is { } cover ? [cover] : null))
            .ToList();
    }

    /// <summary>
    /// The producer id, whether the user typed it or pasted the page they were looking at. Asking
    /// for "just the id" of a site whose ids mostly appear inside URLs is a small cruelty.
    /// </summary>
    internal static string? ProducerIdOf(string? producer)
    {
        if (string.IsNullOrWhiteSpace(producer)) return null;

        var match = ProducerIdRegex().Match(producer.Trim());

        return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : null;
    }

    private static VndbDeveloperTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<VndbDeveloperTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Anchored so a producer id is not read out of the middle of something else.</summary>
    [GeneratedRegex(@"^(?:https?://vndb\.org/)?(?<id>p\d+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProducerIdRegex();
}
