namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 资源配置档案 - 可播放文件配置
/// </summary>
public class ResourceProfilePlayableFileOptions
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
