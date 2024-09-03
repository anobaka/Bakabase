using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;
using Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;
using Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;

namespace Bakabase.Modules.Enhancer.Models.Domain.Constants
{
    public enum EnhancerId
    {
        [Enhancer(typeof(BakabaseEnhancer), PropertyValueScope.BakabaseEnhancer, typeof(BakabaseEnhancerTarget))]
        Bakabase = 1,

        [Enhancer(typeof(ExHentaiEnhancer), PropertyValueScope.ExHentaiEnhancer, typeof(ExHentaiEnhancerTarget))]
        ExHentai = 2,

        [Enhancer(typeof(BangumiEnhancer), PropertyValueScope.BangumiEnhancer, typeof(BangumiEnhancerTarget))]
        Bangumi = 3,

        [Enhancer(typeof(DLsiteEnhancer), PropertyValueScope.BangumiEnhancer, typeof(DLsiteEnhancerTarget))]
        DLsite = 4,

        [Enhancer(typeof(RegexEnhancer), PropertyValueScope.RegexEnhancer, typeof(RegexEnhancerTarget))]
        Regex = 5
    }
}