namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// PathMark 同步状态
/// </summary>
public enum PathMarkSyncStatus
{
    /// <summary>
    /// 待同步
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 同步中
    /// </summary>
    Syncing = 1,

    /// <summary>
    /// 已同步
    /// </summary>
    Synced = 2,

    /// <summary>
    /// 同步失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 待删除（软删除，等待同步后真正删除）
    /// </summary>
    PendingDelete = 4
}
