using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Name = 1,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Tag = 2,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String)]
    Introduction = 3,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.Decimal)]
    Rating = 4,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias], true)]
    OtherPropertiesInLeftPanel = 5,

    [EnhancerTarget(EnhancerTargetType.File, StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias], true)]
    Cover = 6,
}