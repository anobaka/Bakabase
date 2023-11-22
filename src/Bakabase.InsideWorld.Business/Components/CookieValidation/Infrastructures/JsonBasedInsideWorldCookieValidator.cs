using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Resources;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures
{
    public abstract class JsonBasedInsideWorldCookieValidator<TBody> : InsideWorldCookieValidator
    {
        protected JsonBasedInsideWorldCookieValidator(IHttpClientFactory httpClientFactory,
            InsideWorldLocalizer localizer) : base(httpClientFactory, localizer)
        {
        }

        protected override async Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
        {
            var body = await rsp.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<TBody>(body);
            var (success, message) = Validate(data);
            return (success, message, body);
        }

        protected abstract (bool Success, string? Message) Validate(TBody body);
    }
}