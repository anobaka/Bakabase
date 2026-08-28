using Bakabase.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Every path the user has marked. Path marks are the current mechanism for
/// binding filesystem locations to Bakabase entities, so they cover libraries
/// created after the move away from <c>MediaLibraryV2.Paths</c>.
/// </summary>
public class PathMarkServableRootProvider(IServiceScopeFactory scopeFactory) : IServableRootProvider
{
    public async Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPathMarkService>();
        return await service.GetAllPaths();
    }
}
