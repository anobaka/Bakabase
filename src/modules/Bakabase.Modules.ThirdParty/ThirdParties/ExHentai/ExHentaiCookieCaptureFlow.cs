using System;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.ExHentai;

    // Start at the forums login page rather than exhentai.org directly. exhentai.org served
    // without ipb_member_id/ipb_pass_hash returns the Sad Panda page (a near-blank screen with
    // a single `yay=louder` cookie set), which is what users were hitting.
    public string StartUrl => "https://forums.e-hentai.org/index.php?act=Login&CODE=00";

    public string PlatformName => "ExHentai";

    public string[] CookieUrls => ["https://exhentai.org/", "https://forums.e-hentai.org/", "https://e-hentai.org/"];

    // Wipe the Sad Panda marker before navigation begins. exhentai.org checks `yay=louder`
    // first and short-circuits to a blank page when present, ignoring valid igneous/ipb_*.
    // Removing it lets the server re-evaluate auth on the chain hop into exhentai.org.
    public (string Url, string Name)[] StaleCookiesToClear =>
    [
        ("https://exhentai.org/", "yay"),
    ];

    // Browsers scope cookies strictly by domain, so the `ipb_*` / `igneous` cookies the user
    // gets after logging into forums.e-hentai.org sit on `.e-hentai.org` and never get sent
    // on requests to `exhentai.org` (different TLD). Without these on `.exhentai.org`, the
    // server has no auth signal and serves Sad Panda. Mirror them across before each chain
    // hop so the request to exhentai.org carries the credentials that just got established.
    public CookieMirror[] CookieMirrors =>
    [
        new("https://e-hentai.org/", ".exhentai.org",
            ["ipb_member_id", "ipb_pass_hash", "igneous"]),
    ];

    public string? GetNextUrl(string currentUrl)
    {
        if (string.IsNullOrEmpty(currentUrl)) return null;
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        var query = uri.Query ?? "";

        // Only kick off the chain on the explicit login-success URL (`CODE=01`). Any broader
        // condition (e.g. "any forums.e-hentai.org URL without CODE=00") trips when the
        // server auto-redirects the login URL to forums home for a user with stale session
        // cookies — that yanks the user out before they can even see the login form, then
        // strands them on a Sad Panda exhentai page. CODE=01 is the post-form-submit page,
        // so it only fires when the user really did just complete a login.
        if (host == "forums.e-hentai.org" &&
            query.Contains("CODE=01", StringComparison.OrdinalIgnoreCase))
        {
            return "https://e-hentai.org/";
        }

        // From e-hentai.org main site (or its g./www. subdomains the server may bounce us to),
        // hop to exhentai.org. Also covers users with a working session who manually navigate
        // here — they'll get auto-chained to exhentai without going through forums login.
        // forums.e-hentai.org is excluded so this branch doesn't accidentally fire for the
        // forums root (which would skip the e-hentai.org leg and break the cookie handshake).
        if (host != "forums.e-hentai.org" &&
            (host == "e-hentai.org" || host.EndsWith(".e-hentai.org", StringComparison.OrdinalIgnoreCase)))
        {
            return "https://exhentai.org/";
        }

        return null;
    }
}
