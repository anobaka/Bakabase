using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateEnhancerTargetAllInOneOptions : EnhancerTargetOptions
{
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public Property Property { get; set; } = null!;
}