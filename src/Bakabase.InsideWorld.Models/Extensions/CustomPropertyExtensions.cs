using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class CustomPropertyExtensions
    {
        public static Dictionary<string, List<CustomResourceProperty>> Merge(
            this Dictionary<string, List<CustomResourceProperty>> a, Dictionary<string, List<CustomResourceProperty>> b)
        {
            var result = a.ToDictionary(x => x.Key, x => x.Value.Select(y => y with { }).ToList());

            foreach (var (key, listB) in b)
            {
                if (result.TryGetValue(key, out var listA))
                {
                    var restB = listB.ToHashSet();
                    foreach (var cpa in listA)
                    {
                        var cpb = listB.FirstOrDefault(x => x.Index == cpa.Index);
                        if (cpb != null)
                        {
                            restB.Remove(cpb);
                            cpa.Value = cpb.Value;
                        }
                    }

                    listA.AddRange(restB.Select(x => x with { }));
                }
                else
                {
                    result[key] = listB.Select(x => x with { }).ToList();
                }
            }

            return result;
        }
    }
}