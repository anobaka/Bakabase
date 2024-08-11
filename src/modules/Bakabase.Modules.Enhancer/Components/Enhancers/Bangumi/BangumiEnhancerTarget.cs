using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListString,
        [EnhancerTargetOptionsItem.IntegrateWithAlias, EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Tags = 2,

    [EnhancerTarget(StandardValueType.String,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Introduction = 3,

    [EnhancerTarget(StandardValueType.Decimal,
        [EnhancerTargetOptionsItem.AutoGenerateProperties])]
    Rating = 4,

    [EnhancerTarget(StandardValueType.ListString,
        [EnhancerTargetOptionsItem.AutoGenerateProperties, EnhancerTargetOptionsItem.IntegrateWithAlias], true)]
    OtherPropertiesInLeftPanel = 5,

    [EnhancerTarget(StandardValueType.ListString, [])]
    Cover = 6,
}