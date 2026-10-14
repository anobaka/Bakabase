using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Remoting.Components.Console;

/// <summary>Reads this app's profiles through its own service scope, never the managed server's DI container.</summary>
internal sealed class HostSourceResourceProfiles(IServiceScopeFactory scopes) : ISourceResourceProfilePlaybackService
{
    public async Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
        int resourceId, CancellationToken cancellationToken = default)
    {
        return (await ResolveManyAsync(sourceDeviceId, libraryEpoch, [resourceId], cancellationToken))[resourceId];
    }

    public async Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
        string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISourceResourceProfilePlaybackService>()
                .ResolveManyAsync(sourceDeviceId, libraryEpoch, resourceIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                "The local resource profiles could not be matched. Check the source device and its profile filters before retrying.", e);
        }
    }
}
