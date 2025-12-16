namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// 路径标记类型
/// </summary>
public enum PathMarkType
{
    /// <summary>
    /// 资源标识 - 将路径标记为资源
    /// </summary>
    Resource = 1,

    /// <summary>
    /// 属性配置 - 为资源设置属性值
    /// </summary>
    Property = 2,

    /// <summary>
    /// 媒体库关联 - 将资源关联到媒体库
    /// </summary>
    MediaLibrary = 3
}
