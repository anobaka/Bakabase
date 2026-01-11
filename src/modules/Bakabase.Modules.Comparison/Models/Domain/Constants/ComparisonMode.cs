namespace Bakabase.Modules.Comparison.Models.Domain.Constants;

/// <summary>
/// 对比模式
/// </summary>
public enum ComparisonMode
{
    /// <summary>
    /// 完全相等 (A == B)
    /// </summary>
    StrictEqual = 0,

    // NormalizedText = 1 已移除，normalize 现在是独立的预处理选项

    /// <summary>
    /// 编辑距离 / Jaccard 相似度 (0.0-1.0)
    /// </summary>
    TextSimilarity = 2,

    /// <summary>
    /// 正则提取首个数字序列进行整数对比
    /// </summary>
    RegexExtractNumber = 3,

    /// <summary>
    /// 固定容差 Abs(A - B) &lt;= X
    /// </summary>
    FixedTolerance = 4,

    /// <summary>
    /// 相对容差 Abs(A - B) / Max &lt;= X%
    /// </summary>
    RelativeTolerance = 5,

    /// <summary>
    /// 集合交集 Jaccard Index (交集/并集)
    /// </summary>
    SetIntersection = 6,

    /// <summary>
    /// 子集判定 A 包含于 B 或 B 包含于 A
    /// </summary>
    Subset = 7,

    /// <summary>
    /// 时间窗口 时间差在窗口内
    /// </summary>
    TimeWindow = 8,

    /// <summary>
    /// 同一天 日期相同，忽略时间
    /// </summary>
    SameDay = 9,

    /// <summary>
    /// 文件扩展名分布一致
    /// </summary>
    ExtensionMap = 10
}
