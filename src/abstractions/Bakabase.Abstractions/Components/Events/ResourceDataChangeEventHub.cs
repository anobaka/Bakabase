namespace Bakabase.Abstractions.Components.Events;

/// <summary>
/// 资源数据变更事件中心，负责事件的发布和订阅
/// </summary>
public class ResourceDataChangeEventHub : IResourceDataChangeEvent, IResourceDataChangeEventPublisher
{
    public event Action<ResourceDataChangedEventArgs>? OnResourceDataChanged;
    public event Action<ResourceRemovedEventArgs>? OnResourceRemoved;

    public void PublishResourceChanged(int resourceId)
    {
        OnResourceDataChanged?.Invoke(new ResourceDataChangedEventArgs
        {
            ResourceIds = [resourceId]
        });
    }

    public void PublishResourcesChanged(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds as IReadOnlyCollection<int> ?? resourceIds.ToList();
        if (ids.Count > 0)
        {
            OnResourceDataChanged?.Invoke(new ResourceDataChangedEventArgs
            {
                ResourceIds = ids
            });
        }
    }

    public void PublishResourceRemoved(int resourceId)
    {
        OnResourceRemoved?.Invoke(new ResourceRemovedEventArgs
        {
            ResourceIds = [resourceId]
        });
    }

    public void PublishResourcesRemoved(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds as IReadOnlyCollection<int> ?? resourceIds.ToList();
        if (ids.Count > 0)
        {
            OnResourceRemoved?.Invoke(new ResourceRemovedEventArgs
            {
                ResourceIds = ids
            });
        }
    }
}
