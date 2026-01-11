using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class SameDayStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.SameDay;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        if (!TryParseDateTime(value1, out var dt1) || !TryParseDateTime(value2, out var dt2))
            return 0.0;

        return dt1.Date == dt2.Date ? 1.0 : 0.0;
    }

    private static bool TryParseDateTime(object? value, out DateTime result)
    {
        result = DateTime.MinValue;
        if (value == null) return false;

        if (value is DateTime dt)
        {
            result = dt;
            return true;
        }

        return DateTime.TryParse(value.ToString(), out result);
    }
}
