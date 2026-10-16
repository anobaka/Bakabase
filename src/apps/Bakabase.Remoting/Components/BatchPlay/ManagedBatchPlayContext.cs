namespace Bakabase.Remoting.Components.BatchPlay;

/// <summary>Scoped to one unified-library native batch; ordinary managed-window playback leaves this empty.</summary>
public sealed class ManagedBatchPlayContext
{
    public string? LibraryEpoch { get; set; }
}
