using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public enum AvEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    Number,
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    Title,
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    OriginalTitle,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice)]
    Actor,
    [EnhancerTarget(StandardValueType.ListTag, PropertyType.Tags)]
    Tags,
    [EnhancerTarget(StandardValueType.DateTime, PropertyType.Date)]
    Release,
    [EnhancerTarget(StandardValueType.Decimal, PropertyType.Number)]
    Year,
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    Studio,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice)]
    Publisher,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice)]
    Series,
    [EnhancerTarget(StandardValueType.Time, PropertyType.Time)]
    Runtime,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice)]
    Director,
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    Source,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment, [EnhancerTargetOptionsItem.CoverSelectOrder], false, null, ReservedProperty.Cover)]
    Cover,
    [EnhancerTarget(StandardValueType.ListString, PropertyType.Attachment, [EnhancerTargetOptionsItem.CoverSelectOrder], false, null, ReservedProperty.Cover)]
    Poster,
    [EnhancerTarget(StandardValueType.String, PropertyType.SingleLineText)]
    Website,
    [EnhancerTarget(StandardValueType.Boolean, PropertyType.Boolean)]
    Mosaic
}