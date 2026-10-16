using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;

namespace Bakabase.Tests.RemoteAccess;

internal sealed class StubSourceResourceProfiles : ISourceResourceProfilePlaybackService
{
    public Dictionary<int, ResourceProfileOpeningOptions> Matches { get; } = [];
    public List<(string Source, int[] Ids)> Calls { get; } = [];
    public Exception? Failure { get; set; }

    public async Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
        int resourceId, CancellationToken cancellationToken = default) =>
        (await ResolveManyAsync(sourceDeviceId, libraryEpoch, [resourceId], cancellationToken))[resourceId];

    public Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
        string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default)
    {
        Calls.Add((sourceDeviceId, resourceIds.ToArray()));
        if (Failure != null) throw Failure;
        return Task.FromResult<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>>(
            resourceIds.Distinct().ToDictionary(id => id, id => Matches.GetValueOrDefault(id) ?? new()));
    }
}
