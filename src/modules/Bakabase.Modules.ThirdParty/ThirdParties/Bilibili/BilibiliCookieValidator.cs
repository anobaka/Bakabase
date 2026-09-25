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
            // Mid is a long: 16-digit account ids overflowed the former int and failed validation of a valid cookie.
            var success = body is {Code: 0, Data.Profile.Mid: > 0};
            return (success, body?.Message);
        }
    }
}
