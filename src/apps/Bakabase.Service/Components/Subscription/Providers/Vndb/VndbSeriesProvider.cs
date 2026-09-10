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

/// <summary>What a VNDB series subscription watches.</summary>
public record VndbSeriesTarget
{
    /// <summary>A visual novel id (<c>v17</c>), or the page URL it came from.</summary>
    public string VisualNovel { get; set; } = "";

    /// <summary>
    /// Which relations to keep, in VNDB's own words — <c>seq</c> (sequel), <c>preq</c> (prequel),
    /// <c>ser</c> (same series), <c>side</c> (side story). Empty keeps every kind, which for a
    /// long-running franchise is more than anyone wants.
    /// </summary>
    public List<string>? Relations { get; set; }

    /// <summary>
    /// Drop fan works and unofficial ports. On by default: "the series" almost never means those.
    /// </summary>
    public bool OfficialOnly { get; set; } = true;

    /// <summary>Whether the visual novel itself is a member, not only what it is related to.</summary>
    public bool IncludeSelf { get; set; } = true;
}

/// <summary>
/// Everything VNDB says belongs with one visual novel — sequels, prequels, the rest of the series.
/// <para>
/// The clearest case for a catalog source: "the whole of this series" is a statement about the
/// world, and for visual novels VNDB is the most reliable place it is written down.
/// </para>
/// </summary>
public partial class VndbSeriesProvider(VndbClient client) : ISubscriptionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Kind => "vndb.series";
    public string DisplayName => "VNDB Series";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.Catalog;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Vndb;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        return Task.FromResult(target is null || VisualNovelIdOf(target.VisualNovel) is null
            ? SubscriptionValidationResult.Invalid("A VNDB visual novel id (v17) or page URL is required")
            : SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson);

        if (target is null) return "";

        var id = VisualNovelIdOf(target.VisualNovel) ?? target.VisualNovel;
        var relations = target.Relations?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

        return relations is {Count: > 0} ? $"vndb {id} · {string.Join(", ", relations)}" : $"vndb {id}";
    }

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");
        var vnId = VisualNovelIdOf(target.VisualNovel)
                   ?? throw new InvalidOperationException("Invalid visual novel");

        var related = await client.GetRelationsAsync(vnId, target.Relations, target.OfficialOnly, ct);

        var items = related
            .Select(r => new SubscriptionItem(
                r.Id!,
                r.Title,
                $"https://vndb.org/{r.Id}",
                r.Image?.Url is { } cover ? [cover] : null))
            .ToList();

        if (target.IncludeSelf)
        {
            // A series' collection that leaves out the thing the series is named after would be a
            // strange list. It is first because it is what the user typed.
            items.Insert(0, new SubscriptionItem(vnId, null, $"https://vndb.org/{vnId}"));
        }

        return items;
    }

    internal static string? VisualNovelIdOf(string? visualNovel)
    {
        if (string.IsNullOrWhiteSpace(visualNovel)) return null;

        var match = VisualNovelIdRegex().Match(visualNovel.Trim());

        return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : null;
    }

    private static VndbSeriesTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<VndbSeriesTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Anchored: a bare <c>v1</c> reads as "volume 1" in half the file names in a library, so it is
    /// only an id when the whole of what was typed is one.
    /// </summary>
    [GeneratedRegex(@"^(?:https?://vndb\.org/)?(?<id>v\d+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex VisualNovelIdRegex();
}
