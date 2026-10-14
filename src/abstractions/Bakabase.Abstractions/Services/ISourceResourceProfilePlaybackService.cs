using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Matches this device's opening profiles against resources owned by another device.
/// Executables and arguments remain local; only source-owned search criteria cross the boundary.
/// </summary>
public interface ISourceResourceProfilePlaybackService
{
    Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
        int resourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
        string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default);
}
