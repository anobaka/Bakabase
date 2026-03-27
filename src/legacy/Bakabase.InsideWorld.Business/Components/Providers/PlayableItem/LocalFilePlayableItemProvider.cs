using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.PlayableItem;

public class LocalFilePlayableItemProvider : IPlayableItemProvider
{
    private readonly IResourceProfileService _resourceProfileService;
    private readonly ISystemPlayer _systemPlayer;
    private readonly FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> _resourceCacheOrm;
    private readonly ILogger<LocalFilePlayableItemProvider> _logger;

    public LocalFilePlayableItemProvider(
        IResourceProfileService resourceProfileService,
        ISystemPlayer systemPlayer,
        FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> resourceCacheOrm,
        ILogger<LocalFilePlayableItemProvider> logger)
    {
        _resourceProfileService = resourceProfileService;
        _systemPlayer = systemPlayer;
        _resourceCacheOrm = resourceCacheOrm;
        _logger = logger;
    }

    public DataOrigin Origin => DataOrigin.FileSystem;
    public int Priority => 20;

    public bool AppliesTo(DomainResource resource)
    {
        return !string.IsNullOrEmpty(resource.Path);
    }

    public DataStatus GetStatus(DomainResource resource)
    {
        return resource.Cache?.CachedTypes.Contains(ResourceCacheType.PlayableFiles) == true
            ? DataStatus.Ready
            : DataStatus.NotStarted;
    }

    public async Task<PlayableItemProviderResult> GetPlayableItemsAsync(DomainResource resource, CancellationToken ct)
    {
        // Check cache first
        if (resource.Cache?.CachedTypes.Contains(ResourceCacheType.PlayableFiles) == true &&
            resource.Cache.PlayableFilePaths is { } cachedPaths)
        {
            var cachedItems = cachedPaths.Select(p => new Abstractions.Models.Domain.PlayableItem
            {
                Origin = DataOrigin.FileSystem,
                Key = p,
                DisplayName = Path.GetFileName(p)
            }).ToList();

            return new PlayableItemProviderResult(cachedItems);
        }

        if (string.IsNullOrEmpty(resource.Path))
            return new PlayableItemProviderResult([]);

        // Handle missing directory gracefully
        if (!resource.IsFile && !Directory.Exists(resource.Path))
        {
            await CachePlayableFiles(resource.Id, []);
            return new PlayableItemProviderResult([]);
        }

        string[] playableFiles = [];

        try
        {
            // Use ResourceProfile to get effective playable file options
            var playableFileOptions = await _resourceProfileService.GetEffectivePlayableFileOptions(resource);
            if (playableFileOptions?.Extensions != null && playableFileOptions.Extensions.Count > 0)
            {
                // Normalize extensions to ensure they start with a dot
                var extensions = playableFileOptions.Extensions
                    .Select(e => e.StartsWith('.') ? e : $".{e}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                List<string> files;
                if (resource.IsFile)
                {
                    files = [resource.Path];
                }
                else
                {
                    try
                    {
                        files = Directory.EnumerateFiles(resource.Path, "*", SearchOption.AllDirectories).ToList();
                    }
                    catch (DirectoryNotFoundException)
                    {
                        files = [];
                    }
                }

                var result = files.Where(f => extensions.Contains(Path.GetExtension(f))).ToList();

                // Apply file name pattern filter if configured
                if (!string.IsNullOrEmpty(playableFileOptions.FileNamePattern))
                {
                    try
                    {
                        var regex = new Regex(playableFileOptions.FileNamePattern, RegexOptions.IgnoreCase);
                        result = result.Where(f => regex.IsMatch(Path.GetFileName(f))).ToList();
                    }
                    catch
                    {
                        // Invalid regex, ignore the filter
                    }
                }

                playableFiles = result.Select(f => f.StandardizePath()!).ToArray();
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to discover playable files for resource {ResourceId}", resource.Id);
        }

        // Save to cache
        await CachePlayableFiles(resource.Id, playableFiles);

        var items = playableFiles.Select(f => new Abstractions.Models.Domain.PlayableItem
        {
            Origin = DataOrigin.FileSystem,
            Key = f,
            DisplayName = Path.GetFileName(f)
        }).ToList();

        return new PlayableItemProviderResult(items);
    }

    public async Task PlayAsync(DomainResource resource, Abstractions.Models.Domain.PlayableItem item, CancellationToken ct)
    {
        var file = item.Key;
        var playedByCustomPlayer = false;

        var playerOptions = await _resourceProfileService.GetEffectivePlayerOptions(resource);
        if (playerOptions?.Players is { Count: > 0 })
        {
            var fileExtension = Path.GetExtension(file);
            var player =
                playerOptions.Players.FirstOrDefault(p =>
                    p.Extensions?.Contains(fileExtension, StringComparer.OrdinalIgnoreCase) == true) ??
                playerOptions.Players.FirstOrDefault(x => x.Extensions?.Any() != true);
            if (player != null)
            {
                _ = Task.Run(async () =>
                {
                    var template = string.IsNullOrEmpty(player.Command) ? "{0}" : player.Command;
                    var escapedFile = file.Replace("\"", "\\\"");
                    var args = Regex.Replace(template, @"([""']?)\{(\d+)\}([""']?)", match =>
                    {
                        var prefix = match.Groups[1].Value;
                        var suffix = match.Groups[3].Value;
                        var alreadyQuoted = (prefix == "\"" && suffix == "\"") ||
                                            (prefix == "'" && suffix == "'");
                        return alreadyQuoted
                            ? $"{prefix}{escapedFile}{suffix}"
                            : $"\"{escapedFile}\"";
                    });
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo(player.ExecutablePath, args)
                        {
                            UseShellExecute = false
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                });
                playedByCustomPlayer = true;
            }
        }

        if (!playedByCustomPlayer)
        {
            await _systemPlayer.Play(file);
        }
    }

    public async Task InvalidateAsync(int resourceId)
    {
        await _resourceCacheOrm.UpdateAll(c => c.ResourceId == resourceId, x =>
        {
            x.CachedTypes &= ~ResourceCacheType.PlayableFiles;
        });
    }

    private async Task CachePlayableFiles(int resourceId, string[] playableFiles)
    {
        var cache = await _resourceCacheOrm.GetByKey(resourceId, true);
        var isNewCache = cache == null;
        cache ??= new ResourceCacheDbModel { ResourceId = resourceId };

        var serializedPlayableFiles = new ListStringValueBuilder(playableFiles.Length == 0 ? null : playableFiles.ToList()).Value
            ?.SerializeAsStandardValue(StandardValueType.ListString);

        if (cache.PlayableFilePaths != serializedPlayableFiles ||
            !cache.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles))
        {
            cache.PlayableFilePaths = serializedPlayableFiles;
            cache.CachedTypes |= ResourceCacheType.PlayableFiles;
            if (isNewCache)
            {
                await _resourceCacheOrm.Add(cache);
            }
            else
            {
                await _resourceCacheOrm.Update(cache);
            }
        }
    }
}
