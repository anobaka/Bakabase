using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class RegexExtractNumberStrategy : IComparisonStrategy
{
    private const string DefaultPattern = @"(?i)(?:v|vol|ch|ep|第)\.?\s*(\d+)";

    public ComparisonMode Mode => ComparisonMode.RegexExtractNumber;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        var str1 = value1?.ToString();
        var str2 = value2?.ToString();

        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            return 0.0;

        var pattern = DefaultPattern;
        if (!string.IsNullOrEmpty(parameter))
        {
            try
            {
                var param = JsonSerializer.Deserialize<RegexExtractNumberParameter>(parameter);
                if (!string.IsNullOrEmpty(param?.Pattern))
                    pattern = param.Pattern;
            }
            catch
            {
                // 使用默认模式
            }
        }

        var num1 = context.GetExtractedNumber(str1, pattern);
        var num2 = context.GetExtractedNumber(str2, pattern);

        if (!num1.HasValue || !num2.HasValue)
            return 0.0;

        return num1.Value == num2.Value ? 1.0 : 0.0;
    }

    private record RegexExtractNumberParameter
    {
        public string? Pattern { get; set; }
    }
}
