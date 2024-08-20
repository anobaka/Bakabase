using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListTag, CustomPropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Tags = 2,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction = 3,

    [EnhancerTarget(StandardValueType.Decimal, CustomPropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoGenerateProperties], false, typeof(RatingMax10))]
    Rating = 4,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoGenerateProperties], true)]
    OtherPropertiesInLeftPanel = 5,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Cover = 6,
}