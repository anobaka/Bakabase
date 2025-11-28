using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input;

/// <summary>
/// 批量查询第三方内容状态的请求模型
/// </summary>
public class ThirdPartyContentTrackerQueryInputModel
{
    /// <summary>
    /// 网站域名标识
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DomainKey { get; set; } = null!;

    /// <summary>
    /// 过滤规则（可选）
    /// </summary>
    [MaxLength(500)]
    public string? Filter { get; set; }

    /// <summary>
    /// 要查询的内容 ID 列表
    /// </summary>
    [Required]
    public List<string> ContentIds { get; set; } = new();
}
