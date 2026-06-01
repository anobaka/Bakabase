namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

/// <summary>
/// One entry in a subscription's snapshot — used for diff and for notification payloads.
/// The framework only inspects <see cref="Id"/>; the rest is for UI / payload presentation.
/// Providers needing extra fields can stash JSON in <see cref="RawPayloadJson"/>.
/// </summary>
public record SubscriptionItem(
    string Id,
    string? Title = null,
    string? Url = null,
    string? ThumbnailUrl = null,
    string? RawPayloadJson = null);
