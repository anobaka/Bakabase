using Bakabase.InsideWorld.Models.Models.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class CollectionExtensions
    {
        public static List<(T? A, T? B)> PairByString<T>(this IEnumerable<T>? aList, IEnumerable<T>? bList,
            Func<T, string> getKey, decimal? minSimilarity = 0.6m) where T : class => aList.Pair(bList, EqualityComparer<T>.Default, (arg1, arg2) =>
        {
            var str1 = getKey(arg1);
            var str2 = getKey(arg2);
            var r = 1 - (decimal) str1.GetLevenshteinDistance(str2) / Math.Max(str1.Length, str2.Length);
            return r;
        }, minSimilarity);

        public static List<(T? A, T? B)> Pair<T>(this IEnumerable<T>? aList, IEnumerable<T>? bList,
            IEqualityComparer<T>? equalityComparer, Func<T, T, decimal>? getSimilarity, decimal? minSimilarity)
            where T : class
        {
            var tmpA = aList?.ToList() ?? new();
            var tmpB = bList?.ToList() ?? new();

            var pairs = new List<(T? A, T? B)>();

            if (equalityComparer != null)
            {
                foreach (var aData in tmpA)
                {
                    var sameB = tmpB.FirstOrDefault(x => equalityComparer.Equals(x, aData));
                    if (sameB != null)
                    {
                        pairs.Add((aData, sameB));
                        tmpA.Remove(aData);
                        tmpB.Remove(sameB);
                    }
                }
            }

            if (getSimilarity != null)
            {
                foreach (var aData in tmpA)
                {
                    var similarB = tmpB.Select((x, i) => (R: getSimilarity(aData, x), B: x))
                        .Where(a => a.R >= minSimilarity).OrderByDescending(x => x.R)
                        .FirstOrDefault().B;

                    if (similarB != null)
                    {
                        pairs.Add((aData, similarB));
                        tmpA.Remove(aData);
                        tmpB.Remove(similarB);
                    }
                }
            }

            pairs.AddRange(tmpA.Select(aData => ((T?) aData, (T?) null)));
            pairs.AddRange(tmpB.Select(bData => ((T?) null, (T?) bData)));

            return pairs;
        }
    }
}