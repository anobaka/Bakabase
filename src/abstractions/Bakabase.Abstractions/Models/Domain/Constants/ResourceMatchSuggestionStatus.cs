namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// What has been decided about a pair of resources that might be the same work.
/// <para>
/// There is no "confirmed" state: confirming merges the newer resource into the older one, and a
/// deleted resource takes every suggestion naming it. A question whose subject stopped existing is
/// not a decided question, it is no longer a question.
/// </para>
/// </summary>
public enum ResourceMatchSuggestionStatus
{
    /// <summary>Nobody has looked at it yet.</summary>
    Pending = 1,

    /// <summary>
    /// Told apart by the user. Kept rather than deleted so the same pair is never suggested twice.
    /// </summary>
    Dismissed = 2
}
