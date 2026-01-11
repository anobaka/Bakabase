using System.Text.Json;
using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class RelativeToleranceStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.RelativeTolerance;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        if (!TryParseDouble(value1, out var num1) || !TryParseDouble(value2, out var num2))
            return 0.0;

        double tolerancePercent = 0.05; // 默认5%
        if (!string.IsNullOrEmpty(parameter))
        {
            try
            {
                var param = JsonSerializer.Deserialize<RelativeToleranceParameter>(parameter);
                tolerancePercent = param?.TolerancePercent ?? 0.05;
            }
            catch
            {
                // 使用默认容差
            }
        }

        var maxValue = Math.Max(Math.Abs(num1), Math.Abs(num2));
        if (maxValue == 0)
            return num1 == num2 ? 1.0 : 0.0;

        var relativeDiff = Math.Abs(num1 - num2) / maxValue;
        return relativeDiff <= tolerancePercent ? 1.0 : 0.0;
    }

    private static bool TryParseDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;

        return double.TryParse(value.ToString(), out result);
    }

    private record RelativeToleranceParameter
    {
        public double TolerancePercent { get; set; }
    }
}
