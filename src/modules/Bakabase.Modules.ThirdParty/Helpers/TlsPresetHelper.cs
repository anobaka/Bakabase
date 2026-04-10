using System.Text.RegularExpressions;
using HcPresets = HttpCloak.Presets;

namespace Bakabase.Modules.ThirdParty.Helpers;

/// <summary>
/// Maps User-Agent strings to HttpCloak TLS fingerprint presets.
/// The preset must be consistent with the UA to avoid server-side mismatch detection.
/// </summary>
public static class TlsPresetHelper
{
    /// <summary>
    /// All available presets with metadata for UI display and UA matching.
    /// </summary>
    public static readonly TlsPresetInfo[] AvailablePresets =
    [
        // Chrome desktop
        new(HcPresets.Chrome146, "Chrome 146"),
        new(HcPresets.Chrome145, "Chrome 145"),
        new(HcPresets.Chrome144, "Chrome 144"),
        new(HcPresets.Chrome143, "Chrome 143"),
        new(HcPresets.Chrome141, "Chrome 141"),
        new(HcPresets.Chrome133, "Chrome 133"),
        // Chrome platform variants
        new(HcPresets.Chrome146Windows, "Chrome 146 (Windows)"),
        new(HcPresets.Chrome146Linux, "Chrome 146 (Linux)"),
        new(HcPresets.Chrome146MacOS, "Chrome 146 (macOS)"),
        // Chrome mobile
        new(HcPresets.Chrome146Android, "Chrome 146 (Android)"),
        new(HcPresets.Chrome146Ios, "Chrome 146 (iOS)"),
        // Firefox
        new(HcPresets.Firefox133, "Firefox 133"),
        // Safari
        new(HcPresets.Safari18, "Safari 18"),
        new(HcPresets.Safari18Ios, "Safari 18 (iOS)"),
        new(HcPresets.Safari17Ios, "Safari 17 (iOS)"),
    ];

    /// <summary>
    /// Default preset when no match is found.
    /// </summary>
    public const string DefaultPreset = "chrome-146";

    private static readonly Regex ChromeVersionRegex = new(@"Chrome/(\d+)", RegexOptions.Compiled);
    private static readonly Regex FirefoxVersionRegex = new(@"Firefox/(\d+)", RegexOptions.Compiled);
    private static readonly Regex SafariVersionRegex = new(@"Version/(\d+)\.\d+.*Safari/", RegexOptions.Compiled);

    /// <summary>
    /// Infers the best matching HttpCloak preset from a User-Agent string.
    /// </summary>
    public static string InferPresetFromUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return DefaultPreset;

        var isIos = userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
                    || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase);
        var isAndroid = userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase);
        var isMac = userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase);
        var isLinux = !isAndroid && userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase);
        var isWindows = userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase);

        // Check Firefox first (Firefox UA also contains "Safari" token)
        var firefoxMatch = FirefoxVersionRegex.Match(userAgent);
        if (firefoxMatch.Success && !userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            return HcPresets.Firefox133;
        }

        // Check Chrome/Chromium-based browsers (Edge, Chrome, Brave, etc.)
        var chromeMatch = ChromeVersionRegex.Match(userAgent);
        if (chromeMatch.Success)
        {
            var majorVersion = int.Parse(chromeMatch.Groups[1].Value);

            if (isIos) return HcPresets.Chrome146Ios;
            if (isAndroid) return HcPresets.Chrome146Android;

            // Desktop Chrome - match version
            return majorVersion switch
            {
                >= 146 => isWindows ? HcPresets.Chrome146Windows
                    : isLinux ? HcPresets.Chrome146Linux
                    : isMac ? HcPresets.Chrome146MacOS
                    : HcPresets.Chrome146,
                145 => HcPresets.Chrome145,
                144 => HcPresets.Chrome144,
                143 => HcPresets.Chrome143,
                >= 141 => HcPresets.Chrome141,
                _ => HcPresets.Chrome133,
            };
        }

        // Check Safari (real Safari, not Chrome's Safari token)
        var safariMatch = SafariVersionRegex.Match(userAgent);
        if (safariMatch.Success)
        {
            var majorVersion = int.Parse(safariMatch.Groups[1].Value);

            if (isIos)
            {
                return majorVersion >= 18 ? HcPresets.Safari18Ios : HcPresets.Safari17Ios;
            }

            return HcPresets.Safari18;
        }

        return DefaultPreset;
    }
}

public record TlsPresetInfo(string Id, string Label);
