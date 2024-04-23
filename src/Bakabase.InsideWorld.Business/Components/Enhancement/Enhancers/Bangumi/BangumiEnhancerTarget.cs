using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.Bangumi;

public enum BangumiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.SingleLineText)]
    Name = 1,
    [EnhancerTarget(StandardValueType.MultipleChoice)]
    Tag = 2,
    [EnhancerTarget(StandardValueType.MultipleChoice)]
    Original = 3,
    [EnhancerTarget(StandardValueType.Rating)]
    Rating = 4
}