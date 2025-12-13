namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 资源配置档案 - 播放器配置
/// </summary>
public class ResourceProfilePlayerOptions
{
    /// <summary>
    /// 播放器列表
    /// </summary>
    public List<MediaLibraryPlayer>? Players { get; set; }
}
