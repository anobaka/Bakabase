using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplateEnhancerTargetAllInOneOptionsDbModel : EnhancerTargetOptions
{
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
}