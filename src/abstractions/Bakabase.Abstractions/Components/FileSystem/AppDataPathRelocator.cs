using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Components.FileSystem;

public class AppDataPathRelocator : IAppDataPathRelocator
{
    private readonly AppService _appService;
    private readonly IBOptionsManager<AppOptions> _options;

    public AppDataPathRelocator(AppService appService, IBOptionsManager<AppOptions> options)
    {
        _appService = appService;
        _options = options;
        // Self-install into the ambient accessor used by static DB ↔ domain extension methods.
        // The DI container is the single source of truth for the live instance; eagerly
        // resolving this service at startup (e.g. before DB migrations) is enough to
        // activate path resolution everywhere.
        AppDataPaths.Configure(this);
    }

    public string? Resolve(string? stored) =>
        AppDataPathRelocation.Resolve(stored, _appService.AppDataDirectory, CollectOldRoots());

    public string? Relativize(string? path) =>
        AppDataPathRelocation.Relativize(path, _appService.AppDataDirectory, CollectOldRoots());

    private IEnumerable<string?> CollectOldRoots() =>
    [
        _options.Value.PrevDataPath,
        AppDataPathRelocation.TryStripCurrentSegment(_appService.AppDataDirectory),
    ];
}
