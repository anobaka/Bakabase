using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class LanguageExtensions
    {
        public static ResourceLanguage ToLanguage(this string language)
        {
            switch ((language ?? string.Empty).ToUpper())
            {
                case "CN":
                case "ZH-CN":
                case "ZH-HANS":
                case "ZH-HANS-CN":
                    return ResourceLanguage.Chinese;
                case "EN":
                case "EN-US":
                    return ResourceLanguage.English;
                case "JP":
                case "JA":
                case "JA-JP":
                    return ResourceLanguage.Japanese;
                case "KO":
                case "KO-KR":
                    return ResourceLanguage.Korean;
                case "FR":
                case "FR-FR":
                    return ResourceLanguage.French;
                case "DE":
                case "DE-DE":
                    return ResourceLanguage.German;
                case "ES":
                case "ES-ES":
                    return ResourceLanguage.Spanish;
                case "RU":
                case "RU-RU":
                    return ResourceLanguage.Russian;
                default:
                    return ResourceLanguage.NotSet;
            }
        }

        public static string ToShortString(this ResourceLanguage language)
        {
            switch (language)
            {
                case ResourceLanguage.Chinese:
                    return "CN";
                case ResourceLanguage.English:
                    return "EN";
                case ResourceLanguage.NotSet:
                    return null;
                case ResourceLanguage.Japanese:
                    return "JP";
                case ResourceLanguage.Korean:
                    return "KO";
                case ResourceLanguage.French:
                    return "FR";
                case ResourceLanguage.German:
                    return "DE";
                case ResourceLanguage.Spanish:
                    return "ES";
                case ResourceLanguage.Russian:
                    return "RU";
                default:
                    throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }

        public static string BuildLanguageString(this ResourceLanguage language)
        {
            var str = language.ToShortString();
            if (!string.IsNullOrEmpty(str))
            {
                str = "[" + str + "]";
            }

            return str;
        }
    }
}