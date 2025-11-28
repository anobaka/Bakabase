using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 第三方内容追踪记录，用于标记用户在第三方网站上查看的内容
/// </summary>
public record ThirdPartyContentTrackerDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 网站域名标识（多域名网站使用同一个 key）
    /// 例如：pixiv, bilibili, twitter 等
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DomainKey { get; set; } = null!;

    /// <summary>
    /// 过滤规则/分类标识（从 URL 中提取）
    /// 例如：tag、分类、搜索条件等
    /// </summary>
    [MaxLength(500)]
    public string? Filter { get; set; }

    /// <summary>
    /// 内容的唯一标识（在该网站内唯一）
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string ContentId { get; set; } = null!;

    /// <summary>
    /// 内容的更新时间（来自第三方网站）
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 用户查看该内容的时间
    /// </summary>
    public DateTime ViewedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 记录创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
