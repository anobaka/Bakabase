using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 媒体库-资源映射领域模型
/// </summary>
public class MediaLibraryResourceMapping
{
    public int Id { get; set; }
    public int MediaLibraryId { get; set; }
    public int ResourceId { get; set; }

    /// <summary>
    /// 映射来源（规则自动 / 手动配置）
    /// </summary>
    public MappingSource Source { get; set; }

    /// <summary>
    /// 来源规则 ID（如果是规则自动生成）
    /// </summary>
    public int? SourceRuleId { get; set; }

    public DateTime CreateDt { get; set; }
}
