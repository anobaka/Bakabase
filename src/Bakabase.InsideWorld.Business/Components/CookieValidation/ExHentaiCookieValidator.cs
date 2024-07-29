using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ExHentai;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation
{
    public class ExHentaiCookieValidator : NotEmptyResponseInsideWorldCookieValidator
    {
        public ExHentaiCookieValidator(IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer) : base(
            httpClientFactory, localizer)
        {
        }

        public override CookieValidatorTarget Target => CookieValidatorTarget.ExHentai;
        protected override string Url => ExHentaiClient.Domain;
    }
}