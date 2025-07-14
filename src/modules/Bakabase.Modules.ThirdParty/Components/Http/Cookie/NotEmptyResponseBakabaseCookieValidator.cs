using Bakabase.Abstractions.Components.Localization;
using Bootstrap.Extensions;

namespace Bakabase.Modules.ThirdParty.Components.Http.Cookie
{
    public abstract class NotEmptyResponseBakabaseCookieValidator(
        IHttpClientFactory httpClientFactory,
        IBakabaseLocalizer localizer)
        : AbstractCookieValidator(httpClientFactory, localizer)
    {
        protected override async Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
        {
            var body = await rsp.Content.ReadAsStringAsync();
            return (body.IsNotEmpty(), null, body);
        }
    }
}