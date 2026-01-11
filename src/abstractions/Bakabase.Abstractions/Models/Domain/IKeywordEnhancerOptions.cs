namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Options interface for keyword-based enhancers.
/// </summary>
public interface IKeywordEnhancerOptions : IEnhancerBaseOptions
{
    /// <summary>
    /// The property used to generate the search keyword for enhancers that need it.
    /// </summary>
    ScopePropertyKey? KeywordProperty { get; }

    /// <summary>
    /// Whether to preprocess the keyword using special text transformations.
    /// </summary>
    bool? PretreatKeyword { get; }
}
