using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// PathMark 同步服务接口
/// </summary>
public interface IPathMarkSyncService
{
    /// <summary>
    /// 将 mark ids 加入同步队列（FileSystem source）。如果 markIds 为空，则同步所有 pending marks。
    /// </summary>
    /// <param name="markIds">要同步的 mark ID 列表</param>
    Task EnqueueSync(params int[] markIds);

    /// <summary>
    /// 将指定 source 的同步请求加入队列。
    /// 对于非 FileSystem source，markIds 可为空（由 resolver 自行 discover）。
    /// </summary>
    /// <param name="source">要同步的资源来源</param>
    /// <param name="markIds">要同步的 mark ID 列表（仅 FileSystem 有效）</param>
    Task EnqueueSync(ResourceSource source, params int[] markIds);
}
