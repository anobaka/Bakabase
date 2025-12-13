namespace Bakabase.Abstractions.Models.View;

/// <summary>
/// PathMark 同步结果
/// </summary>
public class PathMarkSyncResult
{
    /// <summary>
    /// 创建的资源数量
    /// </summary>
    public int ResourcesCreated { get; set; }

    /// <summary>
    /// 删除的资源数量
    /// </summary>
    public int ResourcesDeleted { get; set; }

    /// <summary>
    /// 应用的属性值数量
    /// </summary>
    public int PropertiesApplied { get; set; }

    /// <summary>
    /// 删除的属性值数量
    /// </summary>
    public int PropertiesDeleted { get; set; }

    /// <summary>
    /// 创建的媒体库映射数量
    /// </summary>
    public int MediaLibraryMappingsCreated { get; set; }

    /// <summary>
    /// 删除的媒体库映射数量
    /// </summary>
    public int MediaLibraryMappingsDeleted { get; set; }

    /// <summary>
    /// 失败的标记数量
    /// </summary>
    public int FailedMarks { get; set; }

    /// <summary>
    /// 错误详情列表
    /// </summary>
    public List<PathMarkSyncError> Errors { get; set; } = new();
}

/// <summary>
/// PathMark 同步错误详情
/// </summary>
public class PathMarkSyncError
{
    /// <summary>
    /// 标记 ID
    /// </summary>
    public int MarkId { get; set; }

    /// <summary>
    /// 标记路径
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = null!;
}
