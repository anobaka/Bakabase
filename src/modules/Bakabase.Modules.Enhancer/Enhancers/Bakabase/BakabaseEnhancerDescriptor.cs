using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Enhancer.Enhancers.Bakabase;

public record BakabaseEnhancerDescriptor() : IEnhancerDescriptor
{
    public EnhancerId Id => EnhancerId.Bakabase;
    public string Name => "Bakabase";
    public string? Description => "Enhancer_BakabaseEnhancerDescription";

    public EnhancerTargetDescriptor[] Targets => SpecificEnumUtils<BakabaseEnhancerTarget>.Values.Select(target =>
    {
        var attr = target.GetAttribute<EnhancerTargetAttribute>();
        return new EnhancerTargetDescriptor((int) target, attr.Name ?? target.ToString(), attr.ValueType,
            attr.Description);
    }).ToArray();
}