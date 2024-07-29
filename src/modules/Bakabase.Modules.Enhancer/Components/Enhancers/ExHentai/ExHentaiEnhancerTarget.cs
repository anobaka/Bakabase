using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String)]
    Name = 1,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.String)]
    Introduction,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.Decimal)]
    Rating,

    [EnhancerTarget(EnhancerTargetType.Data, StandardValueType.ListTag)]
    Tags,

    [EnhancerTarget(EnhancerTargetType.File, StandardValueType.String)]
    Cover
}