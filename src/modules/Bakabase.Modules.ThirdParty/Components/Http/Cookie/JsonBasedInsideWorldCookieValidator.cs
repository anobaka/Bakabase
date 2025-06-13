using Bakabase.Abstractions.Components.Localization;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Components.Http.Cookie
{
    public abstract class JsonBasedInsideWorldCookieValidator<TBody>(
        IHttpClientFactory httpClientFactory,
        IBakabaseLocalizer localizer)
        : AbstractCookieValidator(httpClientFactory, localizer)
    {
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