using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components;

public class EnhancerTargetDescriptor(
    Enum id,
    EnhancerId enhancerId,
    IEnhancerLocalizer localizer,
    StandardValueType valueType,
    bool isDynamic,
    int[]? optionsItems) : IEnhancerTargetDescriptor
{
    public int Id { get; } = (int)(object)id;
    public Enum EnumId { get; } = id;
    public string Name => localizer.Enhancer_TargetName(enhancerId, EnumId);
    public StandardValueType ValueType => valueType;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description => localizer.Enhancer_TargetDescription(enhancerId, EnumId);
    public int[]? OptionsItems => optionsItems;
}