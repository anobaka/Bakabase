using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;

namespace Bakabase.Modules.Enhancer.Enhancers.ExHentai;

public enum ExHentaiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.Decimal)]
    Rating = 1,

    [EnhancerTarget(StandardValueType.ListListString)]
    Tags = 2,
}