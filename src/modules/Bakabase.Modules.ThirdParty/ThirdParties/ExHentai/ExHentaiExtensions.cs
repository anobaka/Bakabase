using System.Globalization;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai
{
    public static class ExHentaiExtensions
    {
        public static bool TryParseFromClassName(string className, out ExHentaiCategory category)
        {
            var match = Regex.Match(className, $@"ct(?<c>.)");
            if (match.Success)
            {
                var cChar = match.Groups["c"].Value;
                var c = int.Parse(cChar, NumberStyles.AllowHexSpecifier);
                category = (ExHentaiCategory)Math.Pow(2, c - 1);
                return true;
            }

            category = default;
            return false;
        }
    }
}