using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Tags = 2,

    [EnhancerTarget(StandardValueType.String, PropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Introduction)]
    Introduction = 3,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoBindProperty], false, typeof(RatingMax10),
        reservedPropertyCandidate: ReservedProperty.Rating)]
    Rating = 4,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty], true)]
    OtherPropertiesInLeftPanel = 5,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder])]
    Cover = 6,
}