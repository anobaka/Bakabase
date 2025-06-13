using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai
{
    public class ExHentaiCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
        : NotEmptyResponseInsideWorldCookieValidator(httpClientFactory, localizer)
    {
        public override CookieValidatorTarget Target => CookieValidatorTarget.ExHentai;
        protected override string Url => ExHentaiClient.Domain;
    }
}