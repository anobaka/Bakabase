using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv
{
    public class PixivCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
        : JsonBasedInsideWorldCookieValidator<PixivBaseResponse>(httpClientFactory, localizer)
    {
        public override CookieValidatorTarget Target => CookieValidatorTarget.Pixiv;
        protected override string Url => PixivClient.LoginStateCheckUrl;

        protected override (bool Success, string Message) Validate(PixivBaseResponse body) =>
            (!body.Error, body.Message);
    }
}