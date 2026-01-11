namespace Bakabase.Modules.Comparison.Models.Domain.Constants;

/// <summary>
/// 空值处理行为（适用于单边空值和双边空值）
/// </summary>
public enum NullValueBehavior
{
    /// <summary>
    /// 跳过此规则，不参与计算
    /// </summary>
    Skip = 0,

    /// <summary>
    /// 视为不匹配 (ruleScore = 0)
    /// </summary>
    Fail = 1,

    /// <summary>
    /// 视为匹配 (ruleScore = 1)
    /// </summary>
    Pass = 2
}
