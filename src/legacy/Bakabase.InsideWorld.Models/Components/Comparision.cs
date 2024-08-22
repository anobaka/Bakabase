using System;
using System.Globalization;

namespace Bakabase.InsideWorld.Models.Components
{
    public class Comparision
    {
        /// <summary>
        /// https://docs.microsoft.com/en-us/dotnet/api/system.globalization.compareoptions?view=net-5.0
        /// </summary>
        public static StringComparer StringComparer = System.StringComparer.Create(CultureInfo.CurrentCulture,
            CompareOptions.IgnoreKanaType | CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace |
            CompareOptions.IgnoreWidth);
    }
}
