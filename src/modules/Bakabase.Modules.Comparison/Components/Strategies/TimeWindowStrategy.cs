using System.Text.Json;
using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class TimeWindowStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.TimeWindow;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        if (!TryParseDateTime(value1, out var dt1) || !TryParseDateTime(value2, out var dt2))
            return 0.0;

        var windowHours = 24.0; // 默认24小时
        if (!string.IsNullOrEmpty(parameter))
        {
            try
            {
                var param = JsonSerializer.Deserialize<TimeWindowParameter>(parameter);
                windowHours = param?.WindowHours ?? 24.0;
            }
            catch
            {
                // 使用默认窗口
            }
        }

        var diff = Math.Abs((dt1 - dt2).TotalHours);
        return diff <= windowHours ? 1.0 : 0.0;
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

    private record TimeWindowParameter
    {
        public double WindowHours { get; set; }
    }
}
