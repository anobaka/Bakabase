using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Name = 1,

    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Tag = 2,

    [EnhancerTarget(StandardValueType.ListString, [EnhancerTargetOptionsItem.IntegrateWithAlias])]
    Original = 3,

    [EnhancerTarget(StandardValueType.Decimal)]
    Rating = 4
}