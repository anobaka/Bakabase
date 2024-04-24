using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.Decimal)]
    Rating = 1,

    [EnhancerTarget(StandardValueType.ListListString)]
    Tags = 2,
}