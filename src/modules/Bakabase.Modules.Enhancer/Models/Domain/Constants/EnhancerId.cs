using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Enhancers.Bakabase;

namespace Bakabase.Modules.Enhancer.Models.Domain.Constants
{
    public enum EnhancerId
    {
        [Enhancer(CustomPropertyValueLayer.BakabaseEnhancer, typeof(BakabaseEnhancerTarget))]
        Bakabase = 1,
        // ExHentai
    }
}
