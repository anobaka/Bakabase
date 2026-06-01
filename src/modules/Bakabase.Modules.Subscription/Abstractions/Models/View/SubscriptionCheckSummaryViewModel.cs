namespace Bakabase.Modules.Subscription.Abstractions.Models.View;

/// <summary>
/// Lightweight result returned by the manual "run now" endpoint so the UI can build a
/// meaningful toast instead of just echoing the subscription's name. Mirrors what the
/// service already persists onto the subscription row (LastChangeAt + LastError) plus
/// the diff counts the user wants to see at a glance.
/// </summary>
public record SubscriptionCheckSummaryViewModel
{
    /// <summary>True the first time this subscription is checked — both notification and
    /// workflow trigger are intentionally suppressed because every existing item would
    /// otherwise look "new". The UI uses this to explain why nothing fired downstream.</summary>
    public bool FirstRun { get; init; }

    public int NewItemCount { get; init; }
    public int UpdatedItemCount { get; init; }

    /// <summary>Provider-thrown message when the check itself failed (the row's LastError is
    /// set in lockstep); null on a clean run.</summary>
    public string? Error { get; init; }
}
