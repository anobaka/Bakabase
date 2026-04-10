using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.ThirdParty.Helpers;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using HttpCloak;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusCookieValidator(
    IBakabaseLocalizer localizer)
    : ICookieValidator
{
    private const string Url = "https://www.north-plus.net/index.php";

    public CookieValidatorTarget Target => CookieValidatorTarget.SoulPlus;

    public async Task<BaseResponse> Validate(string cookie, string? userAgent = null, string? tlsPreset = null)
    {
        var preset = tlsPreset ?? TlsPresetHelper.DefaultPreset;
        var ua = userAgent ?? IThirdPartyHttpClientOptions.DefaultUserAgent;

        string body;
        try
        {
            using var session = new Session(preset: preset);
            var headers = new Dictionary<string, string>
            {
                { "User-Agent", ua },
                { "Cookie", cookie }
            };

            var response = session.Get(Url, headers: headers);
            if (response.StatusCode != 200)
            {
                return BaseResponseBuilder.BuildBadRequest(
                    localizer.CookieValidation_Fail(Url, $"Request failed with status code: {response.StatusCode}",
                        null));
            }

            body = response.Text;
        }
        catch (Exception ex)
        {
            return BaseResponseBuilder.BuildBadRequest(
                localizer.CookieValidation_Fail(Url, ex.Message, null));
        }

        var loggedIn = body.Contains("login.php?action-quit", StringComparison.OrdinalIgnoreCase);

        return loggedIn
            ? BaseResponseBuilder.Ok
            : BaseResponseBuilder.BuildBadRequest(localizer.CookieValidation_Fail(Url, "Not logged in", body));
    }
}
