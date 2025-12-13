namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 资源配置档案领域模型
/// </summary>
public class ResourceProfile
{
    public int Id { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 搜索条件
    /// </summary>
    public SearchCriteria SearchCriteria { get; set; } = new();

    /// <summary>
    /// 名称模板（可选），支持变量如 {Name}, {Layer1}, {Layer2} 等
    /// </summary>
    public string? NameTemplate { get; set; }

    /// <summary>
    /// 增强器配置
    /// </summary>
    public EnhancerSettings? EnhancerSettings { get; set; }

    /// <summary>
    /// 可播放文件配置
    /// </summary>
    public PlayableFileSettings? PlayableFileSettings { get; set; }

    /// <summary>
    /// 播放器配置
    /// </summary>
    public PlayerSettings? PlayerSettings { get; set; }

    /// <summary>
    /// 优先级（多个 Profile 匹配同一资源时使用）
    /// </summary>
    public int Priority { get; set; }

    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
}

/// <summary>
/// 增强器配置
/// </summary>
public class EnhancerSettings
{
    /// <summary>
    /// 启用的增强器 ID 列表
    /// </summary>
    public List<int>? EnhancerIds { get; set; }

    /// <summary>
    /// 增强器特定配置 (EnhancerId -> 配置 JSON)
    /// </summary>
    public Dictionary<int, string>? EnhancerConfigs { get; set; }
}

/// <summary>
/// 可播放文件配置
/// </summary>
public class PlayableFileSettings
{
    /// <summary>
    /// 支持的扩展名列表
    /// </summary>
    public List<string>? Extensions { get; set; }

    /// <summary>
    /// 文件名匹配模式（正则）
    /// </summary>
    public string? FileNamePattern { get; set; }
}

/// <summary>
/// 播放器配置
/// </summary>
public class PlayerSettings
{
    /// <summary>
    /// 播放器列表
    /// </summary>
    public List<MediaLibraryPlayer>? Players { get; set; }
}
