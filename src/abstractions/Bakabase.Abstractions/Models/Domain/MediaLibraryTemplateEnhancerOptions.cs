namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplateEnhancerOptions
{
    public int EnhancerId { get; set; }
    public List<MediaLibraryTemplateEnhancerTargetAllInOneOptions>? TargetOptions { get; set; }
}