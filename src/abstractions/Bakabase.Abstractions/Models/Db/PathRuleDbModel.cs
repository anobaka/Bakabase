namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 路径规则数据库模型
/// </summary>
public record PathRuleDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 规则绑定的根路径
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// 标记列表 JSON (List&lt;PathMark&gt;)
    /// </summary>
    public string MarksJson { get; set; } = null!;

    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
}
