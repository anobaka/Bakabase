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
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

namespace Bakabase.Service.Components.Subscription.Providers.Bangumi;

/// <summary>What a Bangumi relations subscription watches.</summary>
public record BangumiSubjectRelationsTarget
{
    /// <summary>A subject id, or the page URL it came from — both are accepted.</summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// Which kinds of relation to keep, in Bangumi's own Chinese labels ("续集", "系列", …). Empty
    /// keeps every relation, which for a long-running franchise is more than anyone wants.
    /// </summary>
    public List<string>? Relations { get; set; }

    /// <summary>Whether the subject itself is a member, not only what it is related to.</summary>
    public bool IncludeSelf { get; set; } = true;
}

/// <summary>
/// Everything Bangumi says belongs with one subject — sequels, prequels, other entries in the
/// series.
/// <para>
/// The clearest case for a catalog source: "the whole of this series" is a statement about the
/// world, and Bangumi is the most reliable place it is written down. What it lists says nothing
/// about how to get any of it, which is exactly why those members show as missing until a lead
/// turns up.
/// </para>
/// </summary>
public partial class BangumiSubjectRelationsProvider : ISubscriptionProvider
{
    private readonly BangumiClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public BangumiSubjectRelationsProvider(BangumiClient client)
    {
        _client = client;
    }

    public string Kind => "bangumi.subjectRelations";
    public string DisplayName => "Bangumi Series";
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.Catalog;
    public ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Bangumi;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);

        if (target is null || SubjectIdOf(target.Subject) is null)
        {
            return Task.FromResult(
                SubscriptionValidationResult.Invalid("A Bangumi subject id or page URL is required"));
        }

        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson)
    {
        var target = TryParse(targetJson);

        if (target is null) return "";

        var id = SubjectIdOf(target.Subject) ?? target.Subject;
        var relations = target.Relations?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

        return relations is {Count: > 0} ? $"bgm {id} · {string.Join(", ", relations)}" : $"bgm {id}";
    }

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
                     ?? throw new InvalidOperationException("Invalid target payload");
        var subjectId = SubjectIdOf(target.Subject)
                        ?? throw new InvalidOperationException("Invalid subject");

        var related = await _client.GetRelatedSubjectsAsync(subjectId, target.Relations, ct);
        var items = related
            .Select(r => new SubscriptionItem(
                r.Id.ToString(),
                r.DisplayName,
                r.Url,
                r.Images?.Large is { } cover ? [cover] : null))
            .ToList();

        if (target.IncludeSelf)
        {
            // A series' collection that leaves out the thing the series is named after would be a
            // strange list. It is first because it is what the user typed.
            items.Insert(0, new SubscriptionItem(subjectId, null, $"https://bgm.tv/subject/{subjectId}"));
        }

        return items;
    }

    /// <summary>
    /// The subject id, whether the user typed the number or pasted the page they were looking at.
    /// Asking for "just the id" of a site whose ids only appear inside URLs is a small cruelty.
    /// </summary>
    private static string? SubjectIdOf(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;

        var trimmed = subject.Trim();

        if (trimmed.All(char.IsDigit)) return trimmed;

        var match = SubjectUrlRegex().Match(trimmed);

        return match.Success ? match.Groups["id"].Value : null;
    }

    private static BangumiSubjectRelationsTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BangumiSubjectRelationsTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"subject/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectUrlRegex();
}
