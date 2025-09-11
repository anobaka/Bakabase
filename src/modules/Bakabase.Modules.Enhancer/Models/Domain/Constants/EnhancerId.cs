using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.Enhancers.Av;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;
using Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;
using Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;
using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Components.Enhancers.Tmdb;

namespace Bakabase.Modules.Enhancer.Models.Domain.Constants
{
    public enum EnhancerId
    {
        [Enhancer(typeof(BakabaseEnhancer), PropertyValueScope.BakabaseEnhancer, typeof(BakabaseEnhancerTarget), [EnhancerTag.UseKeyword])]
        Bakabase = 1,

        [Enhancer(typeof(ExHentaiEnhancer), PropertyValueScope.ExHentaiEnhancer, typeof(ExHentaiEnhancerTarget), [EnhancerTag.UseKeyword])]
        ExHentai = 2,

        [Enhancer(typeof(BangumiEnhancer), PropertyValueScope.BangumiEnhancer, typeof(BangumiEnhancerTarget), [EnhancerTag.UseKeyword])]
        Bangumi = 3,

        [Enhancer(typeof(DLsiteEnhancer), PropertyValueScope.DLsiteEnhancer, typeof(DLsiteEnhancerTarget), [EnhancerTag.UseKeyword])]
        DLsite = 4,

        [Enhancer(typeof(RegexEnhancer), PropertyValueScope.RegexEnhancer, typeof(RegexEnhancerTarget), [EnhancerTag.UseRegex])]
        Regex = 5,

        [Enhancer(typeof(KodiEnhancer), PropertyValueScope.KodiEnhancer, typeof(KodiEnhancerTarget), [])]
        Kodi = 6,

        [Enhancer(typeof(TmdbEnhancer), PropertyValueScope.TmdbEnhancer, typeof(TmdbEnhancerTarget), [EnhancerTag.UseKeyword])]
        Tmdb = 7,

        [Enhancer(typeof(AvEnhancer), PropertyValueScope.AvEnhancer, typeof(AvEnhancerTarget), [EnhancerTag.UseKeyword])]
        Av = 8
    }
}