using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.Constants;
using NPOI.OpenXmlFormats.Dml.Chart;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai
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