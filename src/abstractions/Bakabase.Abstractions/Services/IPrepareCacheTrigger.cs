namespace Bakabase.Abstractions.Services;

/// <summary>
/// Trigger for PrepareCache task with debounce support.
/// When cache is invalidated, call RequestTrigger() to schedule a PrepareCache task run.
/// Multiple calls within the debounce window (5 seconds) will be merged into a single task run.
/// </summary>
public interface IPrepareCacheTrigger
{
    /// <summary>
    /// Request to trigger PrepareCache task after debounce window (5 seconds).
    /// Multiple calls within the window will be merged.
    /// </summary>
    void RequestTrigger();
}
