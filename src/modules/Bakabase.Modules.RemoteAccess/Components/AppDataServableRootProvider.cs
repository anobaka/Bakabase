using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>
/// Bakabase's own data directory. Covers, generated thumbnails, attachments and
/// enhancer files all live here, and the UI loads them through the same
/// path-addressed endpoints as library files — so a remote client that cannot read
/// this directory sees a grid of broken covers.
/// </summary>
public class AppDataServableRootProvider(IServiceScopeFactory scopeFactory) : IServableRootProvider
{
    public Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var fileManager = scope.ServiceProvider.GetRequiredService<IFileManager>();
        return Task.FromResult<IReadOnlyCollection<string>>([fileManager.BaseDir]);
    }
}
