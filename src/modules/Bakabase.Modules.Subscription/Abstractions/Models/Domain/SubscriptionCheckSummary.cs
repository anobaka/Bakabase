namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

/// <summary>
/// What the manual "run now" path reports back to the caller. Captures the metadata users
/// care about (was anything new, was this the first run, did the provider error out) without
/// the heavyweight item lists carried by <see cref="SubscriptionCheckResult"/>.
/// </summary>
public record SubscriptionCheckSummary
{
    /// <summary>True the very first time a subscription is checked; both downstream
    /// notifications and workflow triggers are intentionally suppressed in that case so
    /// every existing item doesn't get reported as "new".</summary>
    public bool FirstRun { get; init; }

    public int NewItemCount { get; init; }
    public int UpdatedItemCount { get; init; }

    /// <summary>Provider/runtime error message, or null on success.</summary>
    public string? Error { get; init; }
}
