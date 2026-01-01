namespace Bakabase.Abstractions.Services;

/// <summary>
/// PathMark 同步服务接口
/// </summary>
public interface IPathMarkSyncService
{
    /// <summary>
    /// 将 mark ids 加入同步队列。如果 markIds 为空，则同步所有 pending marks。
    /// </summary>
    /// <param name="markIds">要同步的 mark ID 列表</param>
    Task EnqueueSync(params int[] markIds);
}
