namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptions>? TargetOptions { get; set; }

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