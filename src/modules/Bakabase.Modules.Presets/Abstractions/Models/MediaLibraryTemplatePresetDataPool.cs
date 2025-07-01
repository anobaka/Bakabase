using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions.Models.Constants;

namespace Bakabase.Modules.Presets.Abstractions.Models;

public record MediaLibraryTemplatePresetDataPool
{
    public List<ResourceType> ResourceTypes { get; set; } = new();
    public List<Property> Properties { get; set; } = [];
    public List<Enhancer> Enhancers { get; set; } = [];
    public Dictionary<int, List<PresetProperty>> ResourceTypePresetPropertyIds { get; set; } = [];
    public Dictionary<int, List<EnhancerId>> ResourceTypeEnhancerIds { get; set; } = [];

    public record ResourceType(PresetResourceType Type, string Name, MediaType MediaType, string? Description);

    public record Property(PresetProperty Id, string Name, PropertyType Type, string? Description);

    public record Enhancer(
        EnhancerId Id,
        string Name,
        string? Description,
        ReservedProperty[] ReservedProperties,
        PresetProperty[] PresetProperties);
}