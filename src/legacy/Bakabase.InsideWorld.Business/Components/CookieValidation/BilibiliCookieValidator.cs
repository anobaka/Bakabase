using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation
{
    public class BilibiliCookieValidator : JsonBasedInsideWorldCookieValidator<DataWrapper<UserCredential>>
    {
        public BilibiliCookieValidator(IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer) : base(
            httpClientFactory, localizer)
        {
        }

        public override CookieValidatorTarget Target => CookieValidatorTarget.BiliBili;

        protected override string Url => BiliBiliApiUrls.Session;

        protected override (bool Success, string? Message) Validate(DataWrapper<UserCredential> body)
        {
            var mid = body?.Data?.Profile?.Mid;
            return (mid.HasValue, body?.Message);
        }
    }
}