using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerLocalizer
{
    string Enhancer_Name(EnhancerId enhancerId);
    string? Enhancer_Description(EnhancerId enhancerId);
    string Enhancer_TargetName(EnhancerId enhancerId, Enum target);
    string? Enhancer_TargetDescription(EnhancerId enhancerId, Enum target);
}