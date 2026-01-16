namespace Bakabase.Abstractions.Components.Events;

/// <summary>
/// 资源数据变更事件
/// </summary>
public interface IResourceDataChangeEvent
{
    /// <summary>
    /// 资源数据变更时触发，通知订阅者更新索引
    /// </summary>
    event Action<ResourceDataChangedEventArgs>? OnResourceDataChanged;

    /// <summary>
    /// 资源被删除时触发
    /// </summary>
    event Action<ResourceRemovedEventArgs>? OnResourceRemoved;

    /// <summary>
    /// 资源封面变更时触发，通知订阅者清除封面缓存
    /// </summary>
    event Action<ResourceCoverChangedEventArgs>? OnResourceCoverChanged;
}

/// <summary>
/// 资源数据变更事件发布者接口
/// </summary>
public interface IResourceDataChangeEventPublisher
{
    /// <summary>
    /// 发布资源数据变更事件（单个资源）
    /// </summary>
    void PublishResourceChanged(int resourceId);

    /// <summary>
    /// 发布资源数据变更事件（多个资源）
    /// </summary>
    void PublishResourcesChanged(IEnumerable<int> resourceIds);

    /// <summary>
    /// 发布资源删除事件（单个资源）
    /// </summary>
    void PublishResourceRemoved(int resourceId);

    /// <summary>
    /// 发布资源删除事件（多个资源）
    /// </summary>
    void PublishResourcesRemoved(IEnumerable<int> resourceIds);

    /// <summary>
    /// 发布资源封面变更事件（多个资源）
    /// </summary>
    void PublishResourceCoverChanged(IEnumerable<int> resourceIds);
}

/// <summary>
/// 资源数据变更事件参数
/// </summary>
public class ResourceDataChangedEventArgs : EventArgs
{
    public required IReadOnlyCollection<int> ResourceIds { get; init; }
}

/// <summary>
/// 资源删除事件参数
/// </summary>
public class ResourceRemovedEventArgs : EventArgs
{
    public required IReadOnlyCollection<int> ResourceIds { get; init; }
}

/// <summary>
/// 资源封面变更事件参数
/// </summary>
public class ResourceCoverChangedEventArgs : EventArgs
{
    public required IReadOnlyCollection<int> ResourceIds { get; init; }
}
