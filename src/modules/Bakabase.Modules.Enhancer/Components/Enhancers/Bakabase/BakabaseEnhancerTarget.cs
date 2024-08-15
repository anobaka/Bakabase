using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Publisher,

    [EnhancerTarget(StandardValueType.DateTime, CustomPropertyType.Date,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    ReleaseDt,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeName,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    VolumeTitle,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Originals,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleChoice,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Language,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Cover
}