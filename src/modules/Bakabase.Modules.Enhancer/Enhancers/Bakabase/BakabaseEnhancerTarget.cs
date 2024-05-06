using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Name = 1,
    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Publisher,
    [EnhancerTarget(StandardValueType.DateTime)]
    ReleaseDt,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    VolumeName,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    VolumeTitle,
    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Originals,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Language
}