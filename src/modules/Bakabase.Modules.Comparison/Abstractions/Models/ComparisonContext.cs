using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Services;

namespace Bakabase.Modules.Comparison.Abstractions.Models;

/// <summary>
/// 对比上下文，包含运行时缓存
/// </summary>
public class ComparisonContext : IDisposable
{
    private readonly ISpecialTextService _specialTextService;

    // 文本清洗缓存
    private readonly ConcurrentDictionary<string, string> _normalizedTextCache = new();

    // 数字提取缓存
    private readonly ConcurrentDictionary<(string Text, string Pattern), int?> _extractedNumberCache = new();

    public ComparisonContext(ISpecialTextService specialTextService)
    {
        _specialTextService = specialTextService;
    }

    /// <summary>
    /// 获取清洗后的文本（带缓存）
    /// </summary>
    public string GetNormalizedText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return string.Empty;

        return _normalizedTextCache.GetOrAdd(rawText, text =>
        {
            // 同步调用异步方法
            return _specialTextService.Pretreatment(text).GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// 提取数字（带缓存）
    /// </summary>
    public int? GetExtractedNumber(string rawText, string pattern)
    {
        if (string.IsNullOrEmpty(rawText))
            return null;

        var key = (rawText, pattern);
        return _extractedNumberCache.GetOrAdd(key, k =>
        {
            try
            {
                var match = Regex.Match(k.Text, k.Pattern);
                if (match.Success && match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var n))
                    return n;
            }
            catch
            {
                // 正则匹配失败
            }
            return null;
        });
    }

    public void Dispose()
    {
        _normalizedTextCache.Clear();
        _extractedNumberCache.Clear();
    }
}
