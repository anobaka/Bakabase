using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class StrictEqualStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.StrictEqual;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        if (value1 == null && value2 == null)
            return 1.0;

        if (value1 == null || value2 == null)
            return 0.0;

        return value1.Equals(value2) ? 1.0 : 0.0;
    }
}
