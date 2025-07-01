using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions.Models.Constants;

namespace Bakabase.Modules.Presets.Abstractions.Models;

public record MediaLibraryTemplateCompactBuilder
{
    public string Name { get; set; } = null!;
    public PresetResourceType ResourceType { get; set; }
    public PresetProperty[] Properties { get; set; } = [];
    public int ResourceLayer { get; set; }
    public List<PresetProperty?>? LayeredProperties { get; set; }
    public EnhancerId[]? EnhancerIds { get; set; }
}