namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// To be simple, we put all possible options into one model for now, even they may be not suitable for current enhancer.
/// </summary>
public record EnhancerFullOptions
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
}