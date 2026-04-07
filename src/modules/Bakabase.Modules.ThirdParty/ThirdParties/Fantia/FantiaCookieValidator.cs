using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Fantia;

public class FantiaCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
    : AbstractCookieValidator(httpClientFactory, localizer)
{
    public override CookieValidatorTarget Target => CookieValidatorTarget.Fantia;
    protected override string Url => "https://fantia.jp/api/v1/me";

    protected override Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
    {
        var success = rsp.IsSuccessStatusCode;
        return Task.FromResult((success, success ? null : $"HTTP {(int)rsp.StatusCode}", (string?)null));
    }
}
