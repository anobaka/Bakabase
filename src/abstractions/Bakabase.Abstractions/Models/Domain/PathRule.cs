namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 路径规则领域模型
/// </summary>
public class PathRule
{
    public int Id { get; set; }

    /// <summary>
    /// 规则绑定的根路径
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// 标记列表
    /// </summary>
    public List<PathMark> Marks { get; set; } = new();

    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
}
