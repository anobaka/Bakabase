using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Cien;

public class CienCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
    : AbstractCookieValidator(httpClientFactory, localizer)
{
    public override CookieValidatorTarget Target => CookieValidatorTarget.Cien;
    protected override string Url => "https://ci-en.dlsite.com/mypage";

    protected override Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
    {
        var success = rsp.IsSuccessStatusCode;
        return Task.FromResult((success, success ? null : $"HTTP {(int)rsp.StatusCode}", (string?)null));
    }
}
