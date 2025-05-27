using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record MediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptions>? TargetOptions { get; set; }
}