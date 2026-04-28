using System;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.View;

/// <summary>
/// 资源配置档案视图模型
/// </summary>
public record ResourceProfileViewModel
{
    public int Id { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 搜索条件（使用 ViewModel 确保 DbValue 为序列化字符串）
    /// </summary>
    public ResourceSearchViewModel? Search { get; set; }

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
