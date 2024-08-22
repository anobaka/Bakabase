using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Resources;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures
{
    public abstract class NotEmptyResponseInsideWorldCookieValidator : InsideWorldCookieValidator
    {
        protected NotEmptyResponseInsideWorldCookieValidator(IHttpClientFactory httpClientFactory,
            InsideWorldLocalizer localizer) : base(httpClientFactory, localizer)
        {
        }

        protected override async Task<(bool Success, string Message, string Content)> Validate(HttpResponseMessage rsp)
        {
            var body = await rsp.Content.ReadAsStringAsync();
            return (body.IsNotEmpty(), null, body);
        }
    }
}