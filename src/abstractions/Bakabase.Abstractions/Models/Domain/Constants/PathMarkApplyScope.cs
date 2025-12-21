namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// PathMark 应用范围
/// </summary>
public enum PathMarkApplyScope
{
    /// <summary>
    /// 仅对匹配的路径生效
    /// </summary>
    MatchedOnly = 1,

    /// <summary>
    /// 对匹配的路径及其所有子目录生效
    /// </summary>
    MatchedAndSubdirectories = 2
}
