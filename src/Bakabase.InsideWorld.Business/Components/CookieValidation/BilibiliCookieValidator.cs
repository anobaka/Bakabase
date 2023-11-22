using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models.Constants;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation
{
    public class BilibiliCookieValidator : JsonBasedInsideWorldCookieValidator<DataWrapper<UserCredential>>
    {
        public BilibiliCookieValidator(IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer) : base(
            httpClientFactory, localizer)
        {
        }

        public override CookieValidatorTarget Target => CookieValidatorTarget.BiliBili;

        protected override string Url => BiliBiliApiConstants.Session;

        protected override (bool Success, string? Message) Validate(DataWrapper<UserCredential> body)
        {
            var mid = body?.Data?.Profile?.Mid;
            return (mid.HasValue, body?.Message);
        }
    }
}