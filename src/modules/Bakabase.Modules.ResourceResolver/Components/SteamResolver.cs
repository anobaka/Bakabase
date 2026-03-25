using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ResourceResolver.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ResourceResolver.Components;

/// <summary>
/// Discovers Steam games as resources.
/// Uses SteamClient to fetch owned games and matches installed paths.
/// Metadata retrieval is handled by SteamEnhancer.
/// </summary>
public class SteamResolver : IResourceResolver
{
    private readonly ISteamAppService _steamAppService;
    private readonly ILogger<SteamResolver> _logger;

    public SteamResolver(
        ISteamAppService steamAppService,
        ILogger<SteamResolver> logger)
    {
        _steamAppService = steamAppService;
        _logger = logger;
    }

    public ResourceSource Source => ResourceSource.Steam;

    /// <summary>
    /// Discovers resources from cached Steam app data.
    /// The SteamAppService is populated by a background sync task that calls SteamClient.
    /// </summary>
    public async Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct)
    {
        var apps = await _steamAppService.GetAll();
        var resources = new List<ResolvedResource>();

        foreach (var app in apps)
        {
            ct.ThrowIfCancellationRequested();

            var coverUrls = new List<string>
            {
                $"https://cdn.akamai.steamstatic.com/steam/apps/{app.AppId}/header.jpg"
            };

            resources.Add(new ResolvedResource
            {
                SourceKey = app.AppId.ToString(),
                DisplayName = app.Name ?? $"Steam App {app.AppId}",
                Path = app.IsInstalled ? app.InstallPath : null,
                Source = ResourceSource.Steam,
                CoverUrls = coverUrls
            });
        }

        _logger.LogInformation("Steam resolver discovered {Count} resources ({Installed} installed)",
            resources.Count, resources.Count(r => r.Path != null));

        return resources;
    }

    public ResolverConfigurationSchema GetConfigurationSchema()
    {
        return new ResolverConfigurationSchema
        {
            Fields =
            [
                new ResolverConfigField
                {
                    Key = "apiKey",
                    Label = "Steam API Key",
                    Description = "Get your API key from https://steamcommunity.com/dev/apikey",
                    Type = ResolverConfigFieldType.Password,
                    Required = true
                },
                new ResolverConfigField
                {
                    Key = "steamId",
                    Label = "Steam ID",
                    Description = "Your 64-bit Steam ID",
                    Type = ResolverConfigFieldType.String,
                    Required = true
                }
            ]
        };
    }

    public ResolverPlayerConfig? GetDefaultPlayerConfig()
    {
        return new ResolverPlayerConfig
        {
            UriTemplate = "steam://rungameid/{SourceKey}"
        };
    }

    public IPlayableFileSelector? GetPlayableFileSelector()
    {
        // Steam games are launched via steam:// URI, not file selection
        return null;
    }

    public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> fileSystemResources, CancellationToken ct)
    {
        var candidates = new List<MigrationCandidate>();

        foreach (var resource in fileSystemResources)
        {
            if (string.IsNullOrEmpty(resource.Path)) continue;

            // Check if path contains steamapps/common/ pattern
            var pathLower = resource.Path.ToLowerInvariant().Replace('\\', '/');
            var steamAppsIdx = pathLower.IndexOf("steamapps/common/", StringComparison.Ordinal);
            if (steamAppsIdx < 0) continue;

            // Try to find appmanifest file in the steamapps directory
            var steamAppsDir = resource.Path[..(steamAppsIdx + "steamapps".Length)];
            if (!Directory.Exists(steamAppsDir)) continue;

            var gameDirName = resource.Path[(steamAppsIdx + "steamapps/common/".Length)..];
            var slashIdx = gameDirName.IndexOfAny(['/', '\\']);
            if (slashIdx > 0) gameDirName = gameDirName[..slashIdx];

            // Search for matching appmanifest
            try
            {
                var manifests = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");
                foreach (var manifest in manifests)
                {
                    var content = File.ReadAllText(manifest);
                    if (content.Contains($"\"{gameDirName}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract AppId from filename
                        var fileName = Path.GetFileNameWithoutExtension(manifest);
                        var appIdStr = fileName.Replace("appmanifest_", "");
                        if (int.TryParse(appIdStr, out _))
                        {
                            candidates.Add(new MigrationCandidate
                            {
                                OriginalResource = resource,
                                SourceKey = appIdStr,
                                Confidence = 0.9f
                            });
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan appmanifest files in {Dir}", steamAppsDir);
            }
        }

        return Task.FromResult(candidates);
    }

    public async Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct)
    {
        // Migration is handled by the sync flow - just need to update the source records
        _logger.LogInformation("Migrating {Count} resources to Steam source", candidates.Count);
    }

    public async Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys)
    {
        var result = new Dictionary<string, string>();
        var appIds = sourceKeys
            .Where(k => int.TryParse(k, out _))
            .Select(k => int.Parse(k))
            .ToList();

        if (appIds.Count == 0) return result;

        var apps = await _steamAppService.GetByAppIds(appIds);
        foreach (var app in apps)
        {
            var name = app.Name ?? $"Steam App {app.AppId}";
            result[app.AppId.ToString()] = name;
        }

        return result;
    }

    public async Task<List<PlayableItem>> DiscoverPlayableItemsAsync(Resource resource, string sourceKey, CancellationToken ct)
    {
        // Steam games have a single playable item: launch via steam:// URI
        var app = int.TryParse(sourceKey, out var appId)
            ? (await _steamAppService.GetByAppIds([appId])).FirstOrDefault()
            : null;

        var displayName = app?.Name ?? $"Steam App {sourceKey}";

        return
        [
            new PlayableItem
            {
                Source = ResourceSource.Steam,
                Key = sourceKey,
                DisplayName = displayName
            }
        ];
    }

    public Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct)
    {
        var uri = $"steam://rungameid/{item.Key}";
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(uri)
            {
                UseShellExecute = true
            }
        };
        process.Start();
        return Task.CompletedTask;
    }
}
