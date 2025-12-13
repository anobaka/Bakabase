namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 资源配置档案 - 增强器配置
/// </summary>
public class ResourceProfileEnhancerOptions
{
    /// <summary>
    /// 各增强器的配置
    /// </summary>
    public List<EnhancerFullOptions>? Enhancers { get; set; }
}
