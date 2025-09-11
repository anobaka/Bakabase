namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions>? TargetOptions { get; set; }
    public List<int>? Requirements { get; set; }
    public ScopePropertyKey? KeywordProperty { get; set; }
}