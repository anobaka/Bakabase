using System;
using System.Collections.Generic;
using System.Globalization;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Lab
{
    internal class Comparison
    {
        public void Compare()
        {
            var comps = new[] { StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreKanaType) };

            var compList = new List<(string, string)>
            {
                ("ガジ", "ガジ"),
                // ("あ", "ア"),
                // ("t", "ŧ"),
            };
            var x = compList[0].Item1 == compList[0].Item2;
            var y = string.Compare(compList[0].Item1, compList[0].Item2);

            // var comps = new 
            foreach (var stringComparison in SpecificEnumUtils<CompareOptions>.Values)
            {
                foreach (var (a, b) in compList)
                {
                    var v = string.Compare(a, b, CultureInfo.CurrentCulture, stringComparison);
                    // if (v == 0)
                    {
                        Console.WriteLine($"{a}:{b}={v}, {stringComparison}");
                    }
                }
            }

            return;
        }
    }
}
