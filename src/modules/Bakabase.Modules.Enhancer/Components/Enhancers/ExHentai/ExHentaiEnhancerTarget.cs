using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Name = 1,

    [EnhancerTarget(StandardValueType.String, PropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Introduction)]
    Introduction,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Rating)]
    Rating,

    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Tags,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder], reservedPropertyCandidate: ReservedProperty.Cover)]
    Cover
}