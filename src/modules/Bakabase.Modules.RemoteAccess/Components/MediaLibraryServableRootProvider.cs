using Bakabase.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Every directory the user has pointed a media library at. These are the paths
/// resources actually live under, so they are what a remote client legitimately
/// reads.
/// </summary>
public class MediaLibraryServableRootProvider(IServiceScopeFactory scopeFactory) : IServableRootProvider
{
    public async Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMediaLibraryV2Service>();
        var libraries = await service.GetAll();

        // MediaLibraryV2.Paths is on its way out in favour of path marks, but it is
        // still the populated source for existing libraries, so both feed the guard.
#pragma warning disable CS0612
        return libraries.SelectMany(l => l.Paths).ToArray();
#pragma warning restore CS0612
    }
}
