using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam;

public class SteamPlayableItemProvider : IPlayableItemProvider
{
    private readonly ISteamAppService _steamAppService;
    private readonly ILogger<SteamPlayableItemProvider> _logger;

    public SteamPlayableItemProvider(
        ISteamAppService steamAppService,
        ILogger<SteamPlayableItemProvider> logger)
    {
        _steamAppService = steamAppService;
        _logger = logger;
    }

    public DataOrigin Origin => DataOrigin.Steam;
    public int Priority => 10;

    public bool AppliesTo(Resource resource)
    {
        return resource.SourceLinks?.Any(l => l.Source == ResourceSource.Steam) == true;
    }

    public DataStatus GetStatus(Resource resource) => DataStatus.Ready;

    public async Task<PlayableItemProviderResult> GetPlayableItemsAsync(Resource resource, CancellationToken ct)
    {
        var sourceLink = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.Steam);
        if (sourceLink == null)
            return new PlayableItemProviderResult([]);

        var sourceKey = sourceLink.SourceKey;
        string? appName = null;

        if (int.TryParse(sourceKey, out var appId))
        {
            var app = await _steamAppService.GetByAppId(appId);
            appName = app?.Name;
        }

        var displayName = appName ?? $"Steam App {sourceKey}";

        return new PlayableItemProviderResult(
        [
            new PlayableItem
            {
                Origin = DataOrigin.Steam,
                Key = sourceKey,
                DisplayName = displayName
            }
        ]);
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        var uri = $"steam://rungameid/{item.Key}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(uri)
            {
                UseShellExecute = true
            }
        };
        process.Start();
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(int resourceId)
    {
        return Task.CompletedTask;
    }
}
