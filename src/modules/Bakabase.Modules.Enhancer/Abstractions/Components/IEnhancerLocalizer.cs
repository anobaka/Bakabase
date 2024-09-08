using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerLocalizer
{
    string Enhancer_Name(EnhancerId enhancerId);
    string? Enhancer_Description(EnhancerId enhancerId);
    string Enhancer_TargetName(EnhancerId enhancerId, Enum target);
    string? Enhancer_TargetDescription(EnhancerId enhancerId, Enum target);
    string Enhancer_Target_Options_PropertyTypeIsNotSupported(ResourcePropertyType type);
    string Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot(ResourcePropertyType type);
    string Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot(int id);
    string Enhancer_Target_Options_PropertyIdIsNotFoundInReservedResourceProperties(int id);
    string Enhancer_Target_Options_PropertyIdIsNotFoundInCustomResourceProperties(int id);
}