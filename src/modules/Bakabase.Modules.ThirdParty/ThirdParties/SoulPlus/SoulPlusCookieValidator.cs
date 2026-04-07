using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public class SoulPlusCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
    : AbstractCookieValidator(httpClientFactory, localizer)
{
    public override CookieValidatorTarget Target => CookieValidatorTarget.SoulPlus;

    protected override string HttpClientName => InternalOptions.HttpClientNames.SoulPlus;

    protected override string Url => "https://www.north-plus.net/";

    protected override async Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
    {
        var body = await rsp.Content.ReadAsStringAsync();
        if (!rsp.IsSuccessStatusCode)
        {
            return (false, rsp.StatusCode.ToString(), body);
        }

        // Discuz logged-in session: common markers on index / forum home
        var loggedIn = body.Contains("安全退出", StringComparison.Ordinal) ||
                       body.Contains("logout.php", StringComparison.OrdinalIgnoreCase) ||
                       body.Contains("space.php?uid=", StringComparison.OrdinalIgnoreCase);
        return (loggedIn, loggedIn ? null : "Not logged in", body);
    }
}
