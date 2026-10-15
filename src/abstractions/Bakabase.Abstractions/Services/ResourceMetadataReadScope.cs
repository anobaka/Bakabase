using Bootstrap.Components.Orm;

namespace Bakabase.Abstractions.Services;

/// <summary>Fresh, read-only metadata projection, bounded to the supplied resource candidates.</summary>
public sealed class ResourceMetadataReadScope : IDisposable
{
    private static readonly AsyncLocal<ResourceMetadataReadScope?> Current = new();
    private readonly ResourceMetadataReadScope? _previous;
    private readonly PersistedReadScope _persisted;
    private bool _disposed;
    public static IReadOnlyList<int>? ResourceIds => Current.Value?._resourceIds;
    private readonly int[] _resourceIds;
    private ResourceMetadataReadScope(int[] resourceIds)
    {
        if (resourceIds.Length > 100 || resourceIds.Any(id => id <= 0))
            throw new ArgumentOutOfRangeException(nameof(resourceIds));
        _resourceIds = resourceIds.Distinct().ToArray();
        _previous = Current.Value;
        _persisted = PersistedReadScope.Enter();
        Current.Value = this;
    }
    public static ResourceMetadataReadScope Enter(int[] resourceIds) => new(resourceIds);
    public void Dispose()
    {
        if (_disposed) return;
        Current.Value = _previous;
        _persisted.Dispose();
        _disposed = true;
    }
}
