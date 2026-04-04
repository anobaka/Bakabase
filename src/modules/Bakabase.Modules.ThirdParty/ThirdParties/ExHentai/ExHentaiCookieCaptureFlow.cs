using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

/// <summary>
/// ExHentai cookie capture flow.
///
/// Flow:
/// 1. Start at exhentai.org.
/// 2. If logged in: page loads normally. User clicks Confirm manually.
///    If not logged in: exhentai returns 302/blank → detected,
///    navigate to e-hentai forums login page.
/// 3. User logs in at forums.e-hentai.org.
/// 4. After login detected, navigate to exhentai.org for 'igneous' cookie → auto Done.
/// </summary>
public class ExHentaiCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target => CookieValidatorTarget.ExHentai;
    public string StartUrl => "https://exhentai.org/";
    public string Title => "Login to ExHentai";
    public string[] CookieUrls => ["https://exhentai.org/", "https://forums.e-hentai.org/", "https://e-hentai.org/"];

    private enum State { Initial, NeedLogin, WaitingForLogin, BackToExHentai }

    private State _state = State.Initial;
    private string? _lastUrl;

    public CookieCaptureStep OnTick(string currentUrl)
    {
        var urlChanged = currentUrl != _lastUrl;
        _lastUrl = currentUrl;

        switch (_state)
        {
            case State.Initial:
                if (!urlChanged) return CookieCaptureStep.Wait;

                if (currentUrl.Contains("exhentai.org"))
                {
                    // Still on exhentai.org — either loading or already logged in.
                    // User can click Confirm if already logged in.
                    return CookieCaptureStep.Wait;
                }

                // Redirected away from exhentai → not logged in.
                _state = State.NeedLogin;
                return CookieCaptureStep.NavigateTo("https://forums.e-hentai.org/index.php?act=Login");

            case State.NeedLogin:
                _state = State.WaitingForLogin;
                return CookieCaptureStep.Wait;

            case State.WaitingForLogin:
                if (!urlChanged) return CookieCaptureStep.Wait;

                if (currentUrl.Contains("forums.e-hentai.org") &&
                    !currentUrl.Contains("act=Login", StringComparison.OrdinalIgnoreCase))
                {
                    // User logged in (redirected away from login page).
                    _state = State.BackToExHentai;
                    return CookieCaptureStep.NavigateTo("https://exhentai.org/");
                }
                return CookieCaptureStep.Wait;

            case State.BackToExHentai:
                if (!urlChanged) return CookieCaptureStep.Wait;

                if (currentUrl.Contains("exhentai.org"))
                {
                    return CookieCaptureStep.Done;
                }
                return CookieCaptureStep.Wait;
        }

        return CookieCaptureStep.Wait;
    }
}
