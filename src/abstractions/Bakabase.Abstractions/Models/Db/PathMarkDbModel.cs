using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 路径标记数据库模型
/// </summary>
public record PathMarkDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 根路径（规范化后的路径）
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// 标记类型
    /// </summary>
    public PathMarkType Type { get; set; }

    /// <summary>
    /// 优先级（数字越大优先级越高，用于解决同类型 Mark 冲突）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 配置内容 JSON（根据 Type 解析为对应的 Config 类）
    /// </summary>
    public string ConfigJson { get; set; } = null!;

    /// <summary>
    /// 同步状态
    /// </summary>
    public PathMarkSyncStatus SyncStatus { get; set; } = PathMarkSyncStatus.Pending;

    /// <summary>
    /// 最后同步时间
    /// </summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>
    /// 同步错误信息
    /// </summary>
    public string? SyncError { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 过期时间（秒），null 表示永不过期
    /// 同步完成后经过此时间，mark 会被标记为 Pending 状态重新同步
    /// </summary>
    public int? ExpiresInSeconds { get; set; }
}
