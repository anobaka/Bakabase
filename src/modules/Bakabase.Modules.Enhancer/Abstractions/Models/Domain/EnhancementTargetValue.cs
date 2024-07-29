using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

public record EnhancementTargetValue<TTarget>(
    TTarget Target,
    string? DynamicTarget,
    IStandardValueBuilder? ValueBuilder)
{
    public TTarget Target { get; set; } = Target;
    /// <summary>
    /// Required if <see cref="Target"/> is marked as <see cref="EnhancerTargetAttribute.IsDynamic"/>
    /// </summary>
    public string? DynamicTarget { get; set; } = DynamicTarget;
    public IStandardValueBuilder? ValueBuilder { get; set; } = ValueBuilder;
}