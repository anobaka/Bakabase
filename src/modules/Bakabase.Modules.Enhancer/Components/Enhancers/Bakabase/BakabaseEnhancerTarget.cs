using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Publisher,

    [EnhancerTarget(StandardValueType.DateTime, PropertyType.Date, [EnhancerTargetOptionsItem.AutoBindProperty])]
    ReleaseDt,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    VolumeName,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    VolumeTitle,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty])]
    Originals,

    [EnhancerTarget(StandardValueType.String, PropertyType.SingleChoice, [EnhancerTargetOptionsItem.AutoBindProperty])]
    Language,

    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoBindProperty, EnhancerTargetOptionsItem.CoverSelectOrder])]
    Cover
}