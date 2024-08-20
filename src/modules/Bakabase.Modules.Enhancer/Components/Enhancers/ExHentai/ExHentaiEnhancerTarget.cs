using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, CustomPropertyType.SingleLineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(StandardValueType.String, CustomPropertyType.MultilineText,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction,

    [EnhancerTarget(StandardValueType.Decimal, CustomPropertyType.Rating,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Rating,

    [EnhancerTarget(StandardValueType.ListTag, CustomPropertyType.Tags,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Tags,

    [EnhancerTarget(StandardValueType.ListString, CustomPropertyType.Attachment,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Cover
}