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
    /// 搜索条件（复用 ResourceSearch 领域模型）
    /// </summary>
    public ResourceSearch Search { get; set; } = new();

    /// <summary>
    /// 名称模板（可选），支持变量如 {Name}, {Layer1}, {Layer2} 等
    /// </summary>
    public string? NameTemplate { get; set; }

    /// <summary>
    /// 增强器配置
    /// </summary>
    public ResourceProfileEnhancerOptions? EnhancerOptions { get; set; }

    /// <summary>
    /// 可播放文件配置
    /// </summary>
    public ResourceProfilePlayableFileOptions? PlayableFileOptions { get; set; }

    /// <summary>
    /// 播放器配置
    /// </summary>
    public ResourceProfilePlayerOptions? PlayerOptions { get; set; }

    /// <summary>
    /// 属性配置
    /// </summary>
    public ResourceProfilePropertyOptions? PropertyOptions { get; set; }

    /// <summary>
    /// 优先级（多个 Profile 匹配同一资源时使用）
    /// </summary>
    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
