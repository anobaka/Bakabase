using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Presets.Abstractions.Models.Constants;

namespace Bakabase.Modules.Presets.Abstractions.Components;

[AttributeUsage(AttributeTargets.Field)]
public class PresetResourceTypeAttribute(PresetProperty[] properties, EnhancerId[] enhancerIds, MediaType mediaType) : Attribute
{
    public PresetProperty[] Properties { get; } = properties;
    public EnhancerId[] EnhancerIds { get; } = enhancerIds;
    public MediaType MediaType { get; } = mediaType;
}