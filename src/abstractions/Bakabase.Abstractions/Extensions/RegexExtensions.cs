using NPOI.OpenXmlFormats.Spreadsheet;
using NPOI.SS.Formula.Functions;
using System.Text.RegularExpressions;

namespace Bakabase.Abstractions.Extensions;

public static class RegexExtensions
{
    public static Dictionary<string, List<string>> MatchAllAndMergeByNamedGroups(this Regex regex, string input)
    {
        var result = new Dictionary<string, List<string>>();
        var matches = regex.Matches(input);
        if (matches.Any())
        {
            var groupNames = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList();
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                if (match.Success)
                {
                    foreach (var name in groupNames)
                    {
                        if (!result.TryGetValue(name, out var values))
                        {
                            result[name] = values = new List<string>();
                        }

                        values.Add(match.Groups[name].Value);
                    }
                }
            }
        }

        return result;
    }
}