using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input;

/// <summary>
/// 批量标记第三方内容为已查看的请求模型
/// </summary>
public class ThirdPartyContentTrackerMarkViewedInputModel
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
    /// 要标记的内容列表
    /// </summary>
    [Required]
    public List<ContentItem> ContentItems { get; set; } = new();

    public class ContentItem
    {
        /// <summary>
        /// 内容 ID
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string ContentId { get; set; } = null!;

        /// <summary>
        /// 内容的更新时间（可选）
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
