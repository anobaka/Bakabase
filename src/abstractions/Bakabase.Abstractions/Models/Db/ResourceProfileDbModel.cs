namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 资源配置档案数据库模型
/// </summary>
public record ResourceProfileDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 搜索条件 JSON（存储 ResourceSearchDbModel）
    /// </summary>
    public string? SearchJson { get; set; }

    /// <summary>
    /// 名称模板（可选），支持变量如 {Name}, {Layer1}, {Layer2} 等
    /// </summary>
    public string? NameTemplate { get; set; }

    /// <summary>
    /// 增强器配置 JSON
    /// </summary>
    public string? EnhancerSettingsJson { get; set; }

    /// <summary>
    /// 可播放文件配置 JSON
    /// </summary>
    public string? PlayableFileSettingsJson { get; set; }

    /// <summary>
    /// 播放器配置 JSON
    /// </summary>
    public string? PlayerSettingsJson { get; set; }

    /// <summary>
    /// 属性配置 JSON
    /// </summary>
    public string? PropertiesJson { get; set; }

    /// <summary>
    /// 优先级（多个 Profile 匹配同一资源时使用）
    /// </summary>
    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
