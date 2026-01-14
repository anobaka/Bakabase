using System.Text.Json;
using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class FixedToleranceStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.FixedTolerance;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        if (!TryParseDouble(value1, out var num1) || !TryParseDouble(value2, out var num2))
            return 0.0;

        double tolerance = 0;
        if (!string.IsNullOrEmpty(parameter))
        {
            try
            {
                var param = JsonSerializer.Deserialize<FixedToleranceParameter>(parameter);
                tolerance = param?.Tolerance ?? 0;
            }
            catch
            {
                // 使用默认容差
            }
        }

        return Math.Abs(num1 - num2) <= tolerance ? 1.0 : 0.0;
    }

    private static bool TryParseDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;

        return double.TryParse(value.ToString(), out result);
    }

    private record FixedToleranceParameter
    {
        public double Tolerance { get; set; }
    }
}
