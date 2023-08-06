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
                    return ResourceLanguage.Chinese;
                case "EN":
                    return ResourceLanguage.English;
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