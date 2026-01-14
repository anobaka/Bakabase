using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class SubsetStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.Subset;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        var set1 = ConvertToSet(value1);
        var set2 = ConvertToSet(value2);

        if (set1.Count == 0 && set2.Count == 0)
            return 1.0;

        if (set1.Count == 0 || set2.Count == 0)
            return 0.0;

        // A 包含于 B 或 B 包含于 A
        var isSubset = set1.IsSubsetOf(set2) || set2.IsSubsetOf(set1);
        return isSubset ? 1.0 : 0.0;
    }

    private static HashSet<string> ConvertToSet(object? value)
    {
        if (value == null)
            return [];

        if (value is IEnumerable<string> strings)
            return new HashSet<string>(strings, StringComparer.OrdinalIgnoreCase);

        if (value is IEnumerable<object> objects)
            return new HashSet<string>(objects.Select(o => o?.ToString() ?? string.Empty), StringComparer.OrdinalIgnoreCase);

        return [value.ToString() ?? string.Empty];
    }
}
