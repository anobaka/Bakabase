using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation
{
    public class PixivCookieValidator : JsonBasedInsideWorldCookieValidator<PixivBaseResponse>
    {
        public PixivCookieValidator(IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer) : base(
            httpClientFactory, localizer)
        {
        }

        public override CookieValidatorTarget Target => CookieValidatorTarget.Pixiv;
        protected override string Url => PixivClient.LoginStateCheckUrl;

        protected override (bool Success, string Message) Validate(PixivBaseResponse body) =>
            (!body.Error, body.Message);
    }
}