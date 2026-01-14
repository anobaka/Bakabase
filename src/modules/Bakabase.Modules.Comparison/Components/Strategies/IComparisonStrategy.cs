using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Models.Domain.Constants;

namespace Bakabase.Modules.Comparison.Components.Strategies;

public interface IComparisonStrategy
{
    ComparisonMode Mode { get; }

    /// <summary>
    /// 计算两个值的相似度得分
    /// </summary>
    /// <param name="value1">值1</param>
    /// <param name="value2">值2</param>
    /// <param name="parameter">规则参数（JSON字符串）</param>
    /// <param name="context">对比上下文（含缓存）</param>
    /// <returns>0.0 - 1.0 的得分</returns>
    double Calculate(object? value1, object? value2, string? parameter, ComparisonContext context);
}
