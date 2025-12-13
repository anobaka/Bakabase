using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Tasks;

public class GenerateResourceMarkerTask : AbstractPredefinedBTaskBuilder
{
    public GenerateResourceMarkerTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "GenerateResourceMarker";

    public override bool IsEnabled()
    {
        var resourceOptions = ServiceProvider.GetRequiredService<IBOptions<ResourceOptions>>();
        return resourceOptions.Value.KeepResourcesOnPathChange;
    }

    public override Type[] WatchedOptionsTypes => [typeof(ResourceOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var sp = scope.ServiceProvider;

        var resourceService = sp.GetRequiredService<IResourceService>();
        var cacheOrm =
            sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel,
                int>>();

        // Get all folder resources
        var folderResources = await resourceService.GetAll(r => r.CategoryId == 0 && !r.IsFile);

        if (folderResources.Count == 0)
        {
            await args.UpdateTask(t => t.Percentage = 100);
            return;
        }

        var pathResourcesMap = folderResources
            .GroupBy(r => r.Path)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get all resource IDs
        var folderResourceIds = folderResources.Select(r => r.Id).ToHashSet();

        // Get cache entries and find resources that don't have markers yet
        var caches = await cacheOrm.GetAll();
        var cacheMap = caches.ToDictionary(c => c.ResourceId, c => c);

        // Ensure all folder resources have cache entries
        var missingCacheResourceIds = folderResourceIds.Except(cacheMap.Keys).ToList();
        if (missingCacheResourceIds.Any())
        {
            var newCaches = missingCacheResourceIds.Select(id => new ResourceCacheDbModel
            {
                ResourceId = id,
                CachedTypes = 0 // No cache types set yet
            }).ToList();
            await cacheOrm.AddRange(newCaches);
            foreach (var cache in newCaches)
            {
                cacheMap[cache.ResourceId] = cache;
            }
        }

        // Filter resources that don't have ResourceMarkers cache type
        var resourcesToProcess = folderResources
            .Where(r => !cacheMap.TryGetValue(r.Id, out var cache) ||
                        !cache.CachedTypes.HasFlag(ResourceCacheType.ResourceMarkers))
            .ToList();

        if (resourcesToProcess.Count == 0)
        {
            await args.UpdateTask(t => t.Percentage = 100);
            return;
        }

        var processed = 0;
        var pathGroups = resourcesToProcess.GroupBy(r => r.Path).ToArray();
        foreach (var r in pathGroups)
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            var dir = r.First().Path;
            var markerCreated = false;
            var rIds = pathResourcesMap[dir].Select(x => x.Id).ToArray();
            try
            {
                if (Directory.Exists(dir))
                {
                    var marker = Path.Combine(dir, InternalOptions.ResourceMarkerFileName);
                    var content = JsonSerializer.Serialize(new { ids = rIds });
                    await File.WriteAllTextAsync(marker, content);
                    File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.Hidden);
                    markerCreated = true;
                }
            }
            catch
            {
                // ignore per-folder errors
            }

            // Update cache to mark this resource as having a marker
            if (markerCreated)
            {
                try
                {
                    await cacheOrm.UpdateByKeys(rIds,
                        c => c.CachedTypes |= ResourceCacheType.ResourceMarkers);
                }
                catch
                {
                    // ignore cache update errors
                }
            }

            processed++;
            var percent = (int)(processed * 100f / pathGroups.Length);
            await args.UpdateTask(t =>
            {
                t.Percentage = percent;
                t.Process = $"{processed}/{pathGroups.Length}";
            });
        }
    }
}
