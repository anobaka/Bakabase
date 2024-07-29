using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Name = 1,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Publisher,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.DateTime)]
    ReleaseDt,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    VolumeName,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    VolumeTitle,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Originals,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Language
}