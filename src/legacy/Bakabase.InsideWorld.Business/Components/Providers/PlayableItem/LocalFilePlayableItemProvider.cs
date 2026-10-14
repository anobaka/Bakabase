using System;
using System.Collections.Generic;
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
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.Logging;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.PlayableItem;

public class LocalFilePlayableItemProvider : IPlayableItemProvider
{
    private readonly IResourceProfileService _resourceProfileService;
    private readonly ISystemPlayer _systemPlayer;
    private readonly FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> _resourceCacheOrm;
    private readonly IBOptions<UIOptions> _uiOptions;
    private readonly ILogger<LocalFilePlayableItemProvider> _logger;
    private readonly IBatchPlayProcessLauncher _launcher;

    public LocalFilePlayableItemProvider(
        IResourceProfileService resourceProfileService,
        ISystemPlayer systemPlayer,
        FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> resourceCacheOrm,
        IBOptions<UIOptions> uiOptions,
        ILogger<LocalFilePlayableItemProvider> logger,
        IBatchPlayProcessLauncher? launcher = null)
    {
        _resourceProfileService = resourceProfileService;
        _systemPlayer = systemPlayer;
        _resourceCacheOrm = resourceCacheOrm;
        _uiOptions = uiOptions;
        _logger = logger;
        _launcher = launcher ?? new ProcessBatchPlayLauncher(NullLogger<ProcessBatchPlayLauncher>.Instance);
    }

    public DataOrigin Origin => DataOrigin.FileSystem;
    public int Priority => 20;

    /// <summary>
    /// The user-facing "use filesystem playable file cache" switch. While it is off the
    /// cache is neither read nor written — discovery happens live, on demand, every time.
    /// </summary>
    private bool CacheDisabled => _uiOptions.Value.Resource.DisablePlayableFileCache;

    public bool AppliesTo(DomainResource resource)
    {
        return resource.HasLocalPath;
    }

    public DataStatus GetStatus(DomainResource resource)
    {
        // With the cache off there is nothing pre-computed to serve, so the resource is
        // always "not started" — that is what makes the callers (resource list, discovery
        // stream) resolve playable files live instead of trusting a stale cache row.
        if (CacheDisabled)
        {
            return DataStatus.NotStarted;
        }

        return resource.Cache?.CachedTypes.Contains(ResourceCacheType.PlayableFiles) == true
            ? DataStatus.Ready
            : DataStatus.NotStarted;
    }

    public async Task<PlayableItemProviderResult> GetPlayableItemsAsync(DomainResource resource, CancellationToken ct)
    {
        // Check cache first. The CachedTypes flag alone means "already discovered";
        // PlayableFilePaths is null when the resource has zero playable files, which is
        // still a valid cached result and must not trigger a rescan.
        if (!CacheDisabled && resource.Cache?.CachedTypes.Contains(ResourceCacheType.PlayableFiles) == true)
        {
            var cachedItems = (resource.Cache.PlayableFilePaths ?? []).Select(p => new Abstractions.Models.Domain.PlayableItem
            {
                Origin = DataOrigin.FileSystem,
                Key = p,
                DisplayName = Path.GetFileName(p)
            }).ToList();

            return new PlayableItemProviderResult(cachedItems);
        }

        if (!resource.HasLocalPath)
            return new PlayableItemProviderResult([]);


        string[] playableFiles = [];

        try
        {
            // Exists() suppresses permission and I/O errors. A missing/inaccessible
            // resource is a failed discovery, not a successful empty cached result.
            _ = File.GetAttributes(resource.Path);
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
                    // Checked per file, not once around the walk: a recursive enumeration of
                    // a large resource folder is the longest uninterruptible stretch in the
                    // "prepare resource data" task, and until this existed a stop request
                    // (app exit included) had to wait the whole scan out.
                    files = [];
                    foreach (var file in Directory.EnumerateFiles(resource.Path, "*",
                                 SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        files.Add(file);
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
        // An interrupted scan discovered nothing; it did not find nothing. Falling through to the
        // cache write below would persist "this resource has no playable files" — a valid cached
        // result that stops it ever being scanned again.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to discover playable files for resource {ResourceId}", resource.Id);
            throw;
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

        // Resource locations move/disappear between the time we cached
        // PlayableFiles and the time the user clicks play. Without an upfront
        // check, the OS Process.Start blows up with a Win32Exception that
        // lands in Sentry; turning it into a thrown InvalidOperationException
        // here gives the caller a clean, user-facing message instead.
        if (!File.Exists(file) && !Directory.Exists(file))
        {
            throw new InvalidOperationException(
                $"Playable file no longer exists: {file}");
        }

        var playerOptions = await _resourceProfileService.GetEffectivePlayerOptions(resource);
        var player = PlayerFileCapabilities.SelectConfigured(playerOptions, file);
        if (player == null)
        {
            await _systemPlayer.Play(file);
            return;
        }

        var known = KnownPlayerDefinitions.MatchByExecutable(player.ExecutablePath);
        if (known != null && !PlayerFileCapabilities.SupportsFile(known, file))
            throw new InvalidOperationException(
                $"{known.DisplayName} does not support '{PlayerFileCapabilities.Extension(file)}' files. Choose another application in the resource profile.");

        try
        {
            // Keep local custom executables and .lnk shortcuts working as before. Await
            // only the process-start operation: launch errors reach the caller, while
            // a running player never holds the playback request open until it exits.
            await _launcher.LaunchAsync(player.ExecutablePath,
                BatchPlayArguments.BuildFromTemplate(player.Command, file), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Custom player '{Executable}' failed to launch for {File}",
                player.ExecutablePath, file);
            throw new InvalidOperationException($"Could not start the configured player: {ex.Message}", ex);
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
        if (CacheDisabled)
        {
            return;
        }

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
