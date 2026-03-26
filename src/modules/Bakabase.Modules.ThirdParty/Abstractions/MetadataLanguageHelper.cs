namespace Bakabase.Modules.ThirdParty.Abstractions;

/// <summary>
/// Maps app language codes to source-specific API language codes.
/// </summary>
public static class MetadataLanguageHelper
{
    /// <summary>
    /// Supported Steam API language codes.
    /// See: https://partner.steamgames.com/doc/store/localization/languages
    /// </summary>
    public static readonly string[] SteamLanguages =
    [
        "english", "schinese", "tchinese", "japanese", "korean",
        "french", "german", "spanish", "latam", "italian",
        "portuguese", "brazilian", "russian", "thai", "vietnamese"
    ];

    /// <summary>
    /// Get Steam API language code from explicit override or app language fallback.
    /// </summary>
    public static string GetSteamLanguage(string? metadataLanguage, string? appLanguage)
    {
        if (!string.IsNullOrEmpty(metadataLanguage))
            return metadataLanguage;

        return MapAppLanguageToSteam(appLanguage) ?? "english";
    }

    private static string? MapAppLanguageToSteam(string? appLanguage)
    {
        if (string.IsNullOrEmpty(appLanguage)) return null;

        var lang = appLanguage.ToLowerInvariant();
        return lang switch
        {
            "zh-cn" or "cn" => "schinese",
            "zh-tw" => "tchinese",
            "en-us" or "en" => "english",
            "ja-jp" or "ja" => "japanese",
            "ko-kr" or "ko" => "korean",
            "fr-fr" or "fr" => "french",
            "de-de" or "de" => "german",
            "es-es" or "es" => "spanish",
            "pt-br" or "pt" => "brazilian",
            "ru-ru" or "ru" => "russian",
            "it-it" or "it" => "italian",
            "th-th" or "th" => "thai",
            "vi-vn" or "vi" => "vietnamese",
            _ => null
        };
    }
}
