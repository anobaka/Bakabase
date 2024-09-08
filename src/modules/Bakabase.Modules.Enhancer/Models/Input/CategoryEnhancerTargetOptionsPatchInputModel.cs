using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Enhancer.Models.Input;

public record CategoryEnhancerTargetOptionsPatchInputModel
{
    public bool? AutoMatchMultilevelString { get; set; }
    public bool? AutoBindProperty { get; set; }
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public int? PropertyId { get; set; }
    public ResourcePropertyType? PropertyType { get; set; }
    public string? DynamicTarget { get; set; }
}