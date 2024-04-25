using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;

namespace Bakabase.Modules.Enhancer.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String)]
    Name = 1,
    [EnhancerTarget(StandardValueType.ListString)]
    Tag = 2,
    [EnhancerTarget(StandardValueType.ListString)]
    Original = 3,
    [EnhancerTarget(StandardValueType.Decimal)]
    Rating = 4
}