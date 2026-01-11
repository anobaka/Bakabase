using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class ExtensionMapStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.ExtensionMap;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        var extensions1 = ExtractExtensions(value1);
        var extensions2 = ExtractExtensions(value2);

        if (extensions1.Count == 0 && extensions2.Count == 0)
            return 1.0;

        if (extensions1.Count == 0 || extensions2.Count == 0)
            return 0.0;

        // 比较扩展名分布是否一致
        var allKeys = extensions1.Keys.Union(extensions2.Keys).ToList();
        if (allKeys.Count == 0)
            return 1.0;

        var total1 = extensions1.Values.Sum();
        var total2 = extensions2.Values.Sum();

        if (total1 == 0 && total2 == 0)
            return 1.0;

        // 计算分布相似度
        double similarity = 0;
        foreach (var key in allKeys)
        {
            var ratio1 = total1 > 0 ? (double)extensions1.GetValueOrDefault(key, 0) / total1 : 0;
            var ratio2 = total2 > 0 ? (double)extensions2.GetValueOrDefault(key, 0) / total2 : 0;
            similarity += Math.Min(ratio1, ratio2);
        }

        return similarity;
    }

    private static Dictionary<string, int> ExtractExtensions(object? value)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (value == null)
            return result;

        IEnumerable<string> files;
        if (value is IEnumerable<string> strings)
        {
            files = strings;
        }
        else if (value is string str)
        {
            files = [str];
        }
        else
        {
            return result;
        }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file)?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(ext)) continue;

            result.TryGetValue(ext, out var count);
            result[ext] = count + 1;
        }

        return result;
    }
}
