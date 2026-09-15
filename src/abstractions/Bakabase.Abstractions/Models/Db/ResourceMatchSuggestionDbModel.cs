using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// Two resources that might be the same work, waiting for somebody to say whether they are.
/// </summary>
public record ResourceMatchSuggestionDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>
    /// The resource that was just created — the one that would go away if the two are the same.
    /// </summary>
    public int ResourceId { get; set; }

    /// <summary>The resource that was already here.</summary>
    public int CandidateResourceId { get; set; }

    /// <summary>How alike the two titles are, from 0 to 1.</summary>
    public double Score { get; set; }

    /// <summary>
    /// What made this worth asking about, in the user's language — shown next to the pair so the
    /// answer does not have to be guessed from two names that look similar.
    /// </summary>
    [MaxLength(512)]
    public string? Reason { get; set; }

    public ResourceMatchSuggestionStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DecidedAt { get; set; }
}
