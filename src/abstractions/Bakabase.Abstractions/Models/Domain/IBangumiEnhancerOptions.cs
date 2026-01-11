namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Options interface for Bangumi enhancer.
/// </summary>
public interface IBangumiEnhancerOptions : IKeywordEnhancerOptions
{
    /// <summary>
    /// Priority subject type for Bangumi search.
    /// If set, the enhancer will first search with this type;
    /// if no results found, it will fall back to searching all types.
    /// </summary>
    int? BangumiPrioritySubjectType { get; }
}
