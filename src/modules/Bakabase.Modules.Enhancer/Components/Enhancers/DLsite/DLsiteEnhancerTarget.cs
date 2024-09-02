using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;

public enum DLsiteEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Cover,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoGenerateProperties], true)]
    PropertiesOnTheRightSideOfCover,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction,

    [EnhancerTarget(StandardValueType.Decimal, CustomPropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoGenerateProperties], false, typeof(RatingMax10))]
    Rating
}