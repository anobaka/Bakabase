using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

public class BangumiCookieValidator(IHttpClientFactory httpClientFactory, IBakabaseLocalizer localizer)
    : AbstractCookieValidator(httpClientFactory, localizer)
{
    public override CookieValidatorTarget Target => CookieValidatorTarget.Bangumi;

    protected override string HttpClientName => InternalOptions.HttpClientNames.Bangumi;

    /// <summary>
    /// The Bangumi enhancer scrapes the bgm.tv HTML site (see <see cref="BangumiClient"/>), which is
    /// a separate auth system from the api.bgm.tv v0 API. Validate the cookie against the same site
    /// the enhancer actually uses — otherwise a cookie that works for the enhancer is reported invalid.
    /// </summary>
    protected override string Url => "https://bgm.tv/";

    protected override async Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
    {
        if (!rsp.IsSuccessStatusCode)
        {
            return (false, rsp.StatusCode.ToString(), null);
        }

        var body = await rsp.Content.ReadAsStringAsync();
        // bgm.tv renders a logout link (/logout/{hash}) on every page while the session cookie is
        // valid; a logged-out page shows the login form instead.
        var loggedIn = body.Contains("/logout/", StringComparison.Ordinal);
        return (loggedIn, loggedIn ? null : "Not logged in", null);
    }
}
