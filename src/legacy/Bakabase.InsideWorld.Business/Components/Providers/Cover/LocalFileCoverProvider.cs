using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DomainResource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Components.Providers.Cover;

public class LocalFileCoverProvider : ICoverProvider
{
    private readonly ICoverDiscoverer _coverDiscoverer;
    private readonly IFileManager _fileManager;
    private readonly FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> _resourceCacheOrm;
    private readonly ILogger<LocalFileCoverProvider> _logger;

    public LocalFileCoverProvider(
        ICoverDiscoverer coverDiscoverer,
        IFileManager fileManager,
        FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> resourceCacheOrm,
        ILogger<LocalFileCoverProvider> logger)
    {
        _coverDiscoverer = coverDiscoverer;
        _fileManager = fileManager;
        _resourceCacheOrm = resourceCacheOrm;
        _logger = logger;
    }

    public DataOrigin Origin => DataOrigin.FileSystem;
    public int Priority => 20;

    public bool AppliesTo(DomainResource resource) => !string.IsNullOrEmpty(resource.Path);

    public DataStatus GetStatus(DomainResource resource)
    {
        return resource.Cache?.CachedTypes.Contains(ResourceCacheType.Covers) == true
            ? DataStatus.Ready
            : DataStatus.NotStarted;
    }

    public async Task<List<string>?> GetCoversAsync(DomainResource resource, CancellationToken ct)
    {
        // If covers are already cached, return them directly.
        if (resource.Cache?.CachedTypes.Contains(ResourceCacheType.Covers) == true)
        {
            return resource.Cache.CoverPaths is { Count: > 0 } ? resource.Cache.CoverPaths : null;
        }

        if (string.IsNullOrEmpty(resource.Path))
            return null;

        // Handle missing path gracefully
        if (resource.IsFile ? !File.Exists(resource.Path) : !Directory.Exists(resource.Path))
        {
            await MarkCacheReady(resource.Id, null);
            return null;
        }

        // TODO: Get CoverSelectionOrder from ResourceProfile when implemented
        var coverSelectOrder = CoverSelectOrder.FilenameAscending;

        string? coverPath = null;
        try
        {
            var coverDiscoverResult = await _coverDiscoverer.Discover(resource.Path, coverSelectOrder, false, ct);
            if (coverDiscoverResult != null)
            {
                var image = await coverDiscoverResult.LoadByImageSharp(ct);
                var pathWithoutExt = Path.Combine(_fileManager.BuildAbsolutePath("cache", "cover"), resource.Id.ToString())
                    .StandardizePath()!;
                coverPath = await image.SaveAsThumbnail(pathWithoutExt, ct);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to discover/save cover for resource {ResourceId}", resource.Id);
        }

        await MarkCacheReady(resource.Id, coverPath);
        return coverPath != null ? [coverPath] : null;
    }

    public async Task InvalidateAsync(int resourceId)
    {
        await _resourceCacheOrm.UpdateAll(c => c.ResourceId == resourceId, x =>
        {
            x.CachedTypes &= ~ResourceCacheType.Covers;
        });
    }

    private async Task MarkCacheReady(int resourceId, string? coverPath)
    {
        var cache = await _resourceCacheOrm.GetByKey(resourceId, true);
        var isNewCache = cache == null;
        cache ??= new ResourceCacheDbModel { ResourceId = resourceId };

        var serializedCoverPaths = new ListStringValueBuilder(coverPath.IsNullOrEmpty() ? null : [coverPath]).Value!
            .SerializeAsStandardValue(StandardValueType.ListString);

        if (cache.CoverPaths != serializedCoverPaths ||
            !cache.CachedTypes.HasFlag(ResourceCacheType.Covers))
        {
            cache.CoverPaths = serializedCoverPaths;
            cache.CachedTypes |= ResourceCacheType.Covers;
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
