using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Subscription.Abstractions.Components;

/// <summary>
/// A kind of source: something that can be asked what it currently lists.
/// <para>
/// A provider only fetches. What becomes of an item — matched to a resource you already have,
/// turned into one you do not, filed under a collection — is the service's business, and is
/// decided by <see cref="SourceKind"/> rather than by each provider.
/// </para>
/// </summary>
public interface ISubscriptionProvider
{
    /// <summary>Stable identifier used to dispatch from a stored subscription, e.g. "exhentai.search".</summary>
    string Kind { get; }

    /// <summary>Human-readable name for the UI provider picker.</summary>
    string DisplayName { get; }

    /// <summary>Optional icon URL / identifier for the provider picker.</summary>
    string? Icon => null;

    /// <summary>What relationship this source has to the things it lists.</summary>
    SubscriptionSourceKind SourceKind { get; }

    /// <summary>
    /// The identity namespace this source's keys belong to. Null for a sharing channel: an act of
    /// sharing is not an identity, and writing one would claim the post is the work.
    /// </summary>
    ResourceSource? ResourceSource { get; }

    /// <summary>Validate a target payload before persistence.</summary>
    Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct);

    /// <summary>Short summary of a target for list / notification UIs (e.g. "Search: …").</summary>
    string DescribeTarget(string targetJson);

    /// <summary>
    /// Everything the source lists right now, paging exhausted inside the implementation.
    /// <para>
    /// No previous state is handed in: what is new is the difference against the collection's own
    /// members, which the service knows and a provider should not have to.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct);
}
