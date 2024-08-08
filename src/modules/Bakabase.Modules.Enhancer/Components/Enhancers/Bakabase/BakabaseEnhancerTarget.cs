using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Publisher,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.DateTime, [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    ReleaseDt,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeName,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeTitle,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Originals,
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Language
}