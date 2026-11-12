using System.Collections.Concurrent;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>Serializes task plan replacement with retrying the currently associated run.</summary>
public static class AcquisitionTaskGate
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();
    public static SemaphoreSlim For(int taskId) => Gates.GetOrAdd(taskId, _ => new(1, 1));
}
