using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class SetIntersectionStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.SetIntersection;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        var set1 = ConvertToSet(value1);
        var set2 = ConvertToSet(value2);

        if (set1.Count == 0 && set2.Count == 0)
            return 1.0;

        if (set1.Count == 0 || set2.Count == 0)
            return 0.0;

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> ConvertToSet(object? value)
    {
        if (value == null)
            return [];

        if (value is IEnumerable<string> strings)
            return new HashSet<string>(strings, StringComparer.OrdinalIgnoreCase);

        if (value is IEnumerable<object> objects)
            return new HashSet<string>(objects.Select(o => o?.ToString() ?? string.Empty), StringComparer.OrdinalIgnoreCase);

        // 单个值作为单元素集合
        return [value.ToString() ?? string.Empty];
    }
}
