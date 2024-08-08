using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.Decimal,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Rating,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListTag,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Tags,

    [EnhancerTarget(EnhancerTargetType.File, StandardValueType.String)]
    Cover
}