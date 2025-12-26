using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// 资源搜索倒排索引服务
/// </summary>
public interface IResourceSearchIndexService
{
    /// <summary>
    /// 索引是否就绪
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// 索引版本号
    /// </summary>
    long Version { get; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    DateTime LastUpdatedAt { get; }

    /// <summary>
    /// 根据过滤条件查询匹配的资源ID
    /// </summary>
    /// <param name="group">过滤条件组</param>
    /// <returns>
    /// null: 索引未就绪或无过滤条件，应回退到全量搜索
    /// empty: 无匹配资源
    /// non-empty: 匹配的资源ID集合
    /// </returns>
    Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group);

    /// <summary>
    /// 标记资源需要重新索引（非阻塞，立即返回）
    /// </summary>
    void InvalidateResource(int resourceId);

    /// <summary>
    /// 批量标记资源需要重新索引
    /// </summary>
    void InvalidateResources(IEnumerable<int> resourceIds);

    /// <summary>
    /// 删除资源索引
    /// </summary>
    void RemoveResource(int resourceId);

    /// <summary>
    /// 批量删除资源索引
    /// </summary>
    void RemoveResources(IEnumerable<int> resourceIds);

    /// <summary>
    /// 全量重建索引
    /// </summary>
    Task RebuildAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 等待索引就绪
    /// </summary>
    Task WaitForReadyAsync(TimeSpan? timeout = null);

    /// <summary>
    /// 获取索引状态信息
    /// </summary>
    ResourceSearchIndexStatus GetStatus();
}

/// <summary>
/// 索引状态信息
/// </summary>
public class ResourceSearchIndexStatus
{
    public bool IsReady { get; set; }
    public long Version { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public int TotalResourceCount { get; set; }
    public int PendingUpdateCount { get; set; }
    public Dictionary<string, int> IndexSizes { get; set; } = new();
}
