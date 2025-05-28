using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions : EnhancerTargetOptions
{
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
}