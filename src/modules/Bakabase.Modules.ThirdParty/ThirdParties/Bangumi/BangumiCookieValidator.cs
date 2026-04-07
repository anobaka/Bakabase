using System.Net;
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

    /// <summary>GET /v0/me returns current user when cookie is valid (Bearer-style session).</summary>
    protected override string Url => "https://api.bgm.tv/v0/me";

    protected override async Task<(bool Success, string? Message, string? Content)> Validate(HttpResponseMessage rsp)
    {
        var body = await rsp.Content.ReadAsStringAsync();
        if (rsp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (false, "Unauthorized", body);
        }

        if (!rsp.IsSuccessStatusCode)
        {
            return (false, rsp.StatusCode.ToString(), body);
        }

        var ok = body.Contains("\"username\"", StringComparison.Ordinal) ||
                 body.Contains("\"nickname\"", StringComparison.Ordinal);
        return (ok, ok ? null : "Unexpected response", body);
    }
}
