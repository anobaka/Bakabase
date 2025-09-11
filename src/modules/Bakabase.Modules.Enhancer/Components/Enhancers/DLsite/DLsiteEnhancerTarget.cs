using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;

public enum DLsiteEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Name,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder], reservedPropertyCandidate: ReservedProperty.Cover)]
    Cover,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty], true)]
    PropertiesOnTheRightSideOfCover,

    [EnhancerTarget(StandardValueType.String, PropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Introduction)]
    Introduction,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoBindProperty], false, 
        reservedPropertyCandidate: ReservedProperty.Rating)]
    Rating
}