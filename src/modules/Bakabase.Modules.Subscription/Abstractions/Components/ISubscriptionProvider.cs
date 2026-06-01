using Bakabase.Modules.Subscription.Abstractions.Models.Domain;

namespace Bakabase.Modules.Subscription.Abstractions.Components;

/// <summary>
/// A subscription "kind" — knows how to read a target (URL / feed / user-id) and
/// produce a snapshot, plus how to diff that snapshot against the previous one.
/// </summary>
public interface ISubscriptionProvider
{
    /// <summary>Stable identifier used to dispatch from a stored subscription, e.g. "exhentai.search".</summary>
    string Kind { get; }

    /// <summary>Human-readable name for the UI provider picker.</summary>
    string DisplayName { get; }

    /// <summary>Optional icon URL / identifier for the provider picker.</summary>
    string? Icon => null;

    /// <summary>Validate a target payload before persistence.</summary>
    Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct);

    /// <summary>Short summary of a target for list / notification UIs (e.g. "Search: …").</summary>
    string DescribeTarget(string targetJson);

    /// <summary>Fetch the current state and diff it against the previous snapshot.</summary>
    Task<SubscriptionCheckResult> CheckAsync(
        SubscriptionRecord subscription,
        SubscriptionSnapshot? lastSnapshot,
        CancellationToken ct);
}
