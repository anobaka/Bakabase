namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Contains all possible options for enhancers. Implements specific option interfaces
/// to provide type-safe access for different enhancer types.
/// </summary>
public record EnhancerFullOptions : IRegexEnhancerOptions, IBangumiEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<EnhancerTargetFullOptions>? TargetOptions { get; set; }

    /// <summary>
    /// For regex enhancer
    /// </summary>
    public List<string>? Expressions { get; set; }

    /// <summary>
    /// Run this enhancer only after these enhancers have completed.
    /// Values are enhancer ids.
    /// </summary>
    public List<int>? Requirements { get; set; }

    /// <summary>
    /// The property used to generate the search keyword for enhancers that need it.
    /// </summary>
    public ScopePropertyKey? KeywordProperty { get; set; }

    public bool? PretreatKeyword { get; set; }

    /// <summary>
    /// Priority subject type for Bangumi search.
    /// If set, the enhancer will first search with this type;
    /// if no results found, it will fall back to searching all types.
    /// Values correspond to BangumiSubjectType enum.
    /// </summary>
    public int? BangumiPrioritySubjectType { get; set; }
}