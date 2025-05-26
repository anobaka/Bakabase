using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;

public record SharedMediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public EnhancerFullOptions? Options { get; set; }
}