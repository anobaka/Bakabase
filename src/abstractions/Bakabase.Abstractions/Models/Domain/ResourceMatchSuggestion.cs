using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// A pair of resources that might be the same work, with both names, so the list can be read
/// without opening either of them.
/// </summary>
public record ResourceMatchSuggestion
{
    public int Id { get; set; }

    /// <summary>The newly created resource — the one that goes away if the pair is confirmed.</summary>
    public int ResourceId { get; set; }

    public string? ResourceName { get; set; }

    /// <summary>The resource that was already here, and that would keep everything.</summary>
    public int CandidateResourceId { get; set; }

    public string? CandidateResourceName { get; set; }

    public double Score { get; set; }

    public string? Reason { get; set; }

    public ResourceMatchSuggestionStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
