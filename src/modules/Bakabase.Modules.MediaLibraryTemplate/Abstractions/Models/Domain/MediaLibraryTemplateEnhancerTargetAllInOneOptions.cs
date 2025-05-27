using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record MediaLibraryTemplateEnhancerTargetAllInOneOptions : EnhancerTargetOptions
{
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public Bakabase.Abstractions.Models.Domain.Property? Property { get; set; }
}