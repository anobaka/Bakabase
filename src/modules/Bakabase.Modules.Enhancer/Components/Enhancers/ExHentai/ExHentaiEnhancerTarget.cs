using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(StandardValueType.String,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction,

    [EnhancerTarget(StandardValueType.Decimal,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Rating,

    [EnhancerTarget(StandardValueType.ListTag,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Tags,

    [EnhancerTarget(StandardValueType.ListString, null, false)]
    Cover
}