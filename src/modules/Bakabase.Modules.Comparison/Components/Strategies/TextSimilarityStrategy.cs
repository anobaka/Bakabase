using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public class TextSimilarityStrategy : IComparisonStrategy
{
    public ComparisonMode Mode => ComparisonMode.TextSimilarity;

    public double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context)
    {
        var str1 = value1?.ToString() ?? string.Empty;
        var str2 = value2?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(str1) && string.IsNullOrEmpty(str2))
            return 1.0;

        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            return 0.0;

        // 使用 Jaccard 相似度
        return CalculateJaccardSimilarity(str1, str2);
    }

    private static double CalculateJaccardSimilarity(string s1, string s2)
    {
        var set1 = new HashSet<char>(s1.ToLowerInvariant());
        var set2 = new HashSet<char>(s2.ToLowerInvariant());

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();

        return union == 0 ? 1.0 : (double)intersection / union;
    }
}
