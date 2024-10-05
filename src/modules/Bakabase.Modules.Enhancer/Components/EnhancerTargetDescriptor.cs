using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components;

public class EnhancerTargetDescriptor(
    Enum id,
    EnhancerId enhancerId,
    IEnhancerLocalizer localizer,
    StandardValueType valueType,
    PropertyType propertyType,
    bool isDynamic,
    int[]? optionsItems,
    IEnhancementConverter? converter = null,
    ReservedProperty? reservedPropertyCandidate = null
) : IEnhancerTargetDescriptor
{
    public int Id { get; } = (int) (object) id;
    public Enum EnumId { get; } = id;
    public PropertyType PropertyType { get; } = propertyType;
    public string Name => localizer.Enhancer_TargetName(enhancerId, EnumId);
    public StandardValueType ValueType => valueType;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description => localizer.Enhancer_TargetDescription(enhancerId, EnumId);
    public int[]? OptionsItems => optionsItems;
    public IEnhancementConverter? EnhancementConverter => converter;
    public ReservedProperty? ReservedPropertyCandidate => reservedPropertyCandidate;
}