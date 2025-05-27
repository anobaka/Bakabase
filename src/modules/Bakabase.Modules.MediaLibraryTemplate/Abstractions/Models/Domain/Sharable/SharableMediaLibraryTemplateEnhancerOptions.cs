namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions>? TargetOptions { get; set; }
}