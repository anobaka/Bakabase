using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Steam;

public enum SteamEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Name = 1,

    [EnhancerTarget(StandardValueType.String, PropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoBindProperty],
        reservedPropertyCandidate: ReservedProperty.Introduction)]
    Description,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Developer,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Publisher,

    [EnhancerTarget(StandardValueType.DateTime, PropertyType.Date,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    ReleaseDate,

    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Genre,

    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Category,

    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    MetacriticScore,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder],
        reservedPropertyCandidate: ReservedProperty.Cover)]
    Cover,
}
