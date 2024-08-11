using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,
    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Publisher,
    [EnhancerTarget(StandardValueType.DateTime, [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    ReleaseDt,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeName,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeTitle,
    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Originals,
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Language,
    [EnhancerTarget(StandardValueType.ListString, null, false)]
    Cover
}