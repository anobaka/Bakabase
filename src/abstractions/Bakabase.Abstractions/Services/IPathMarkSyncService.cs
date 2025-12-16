using Bakabase.Abstractions.Models.View;
using Bootstrap.Components.Tasks;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// PathMark 同步服务接口
/// </summary>
public interface IPathMarkSyncService
{
    /// <summary>
    /// 启动同步所有 pending marks 的任务
    /// </summary>
    Task StartSyncAll();

    /// <summary>
    /// 启动同步指定 marks 的任务（立即同步用）
    /// </summary>
    /// <param name="markIds">要同步的 mark ID 列表</param>
    Task StartSyncImmediate(int[] markIds);

    /// <summary>
    /// 核心同步逻辑（由 BTask 调用）
    /// </summary>
    /// <param name="markIds">要同步的 mark ID 列表，null 表示所有 pending marks</param>
    /// <param name="onProgressChange">进度变更回调 (0-100)</param>
    /// <param name="onProcessChange">处理信息变更回调</param>
    /// <param name="pt">暂停令牌</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>同步结果</returns>
    Task<PathMarkSyncResult> SyncMarks(
        int[]? markIds,
        Func<int, Task>? onProgressChange,
        Func<string?, Task>? onProcessChange,
        PauseToken pt,
        CancellationToken ct);

    /// <summary>
    /// 停止当前同步任务
    /// </summary>
    Task StopSync();
}
