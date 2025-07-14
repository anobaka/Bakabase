using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili
{
    public class BilibiliCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
        : JsonBasedBakabaseCookieValidator<DataWrapper<UserCredential>>(httpClientFactory, localizer)
    {
        public override CookieValidatorTarget Target => CookieValidatorTarget.BiliBili;

        protected override string Url => BiliBiliApiUrls.Session;

        protected override (bool Success, string? Message) Validate(DataWrapper<UserCredential> body)
        {
            var mid = body?.Data?.Profile?.Mid;
            return (mid.HasValue, body?.Message);
        }
    }
}