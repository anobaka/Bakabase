using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;

namespace Bakabase.Service.Components.Tasks;

public class PredefinedTasksProvider
{
    public PredefinedTasksProvider(IBakabaseLocalizer localizer, IServiceProvider serviceProvider)
    {
        var simpleTaskBuilders = new Dictionary<string, Func<BTaskArgs, IServiceProvider, Task>>()
        {
            {
                "Enhancement", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IEnhancerService>();
                    await service.EnhanceAll(async p => await args.UpdateTask(t => t.Percentage = p),
                        async p => await args.UpdateTask(t => t.Process = p), args.PauseToken,
                        args.CancellationToken);
                }
            },
            {
                "PrepareCache", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IResourceService>();
                    await service.PrepareCache(async p => await args.UpdateTask(t => t.Percentage = p),
                        async p => await args.UpdateTask(t => t.Process = p), args.PauseToken,
                        args.CancellationToken);
                }
            },
            {
                "MoveFiles", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IFileMover>();
                    await service.MovingFiles(async p => await args.UpdateTask(t => t.Percentage = p), args.PauseToken,
                        args.CancellationToken);
                }
            },
            {
                "GenerateResourceMarker", async (args, sp) =>
                {
                    // Note: The enable/disable check has been moved to task definition
                    // This task will only be enqueued when KeepResourcesOnPathChange is true

                    var resourceService = sp.GetRequiredService<IResourceService>();
                    var cacheOrm =
                        sp.GetRequiredService<FullMemoryCacheResourceService<InsideWorldDbContext, ResourceCacheDbModel,
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
        };

        // Define tasks with their enable conditions and dependencies
        TaskDefinitions = simpleTaskBuilders.Select(x => new PredefinedTaskDefinition
        {
            Id = x.Key,
            IsEnabled = () =>
            {
                // Define enable conditions for each task
                switch (x.Key)
                {
                    case "GenerateResourceMarker":
                        var resourceOptions = serviceProvider.GetRequiredService<IBOptions<ResourceOptions>>();
                        return resourceOptions.Value.KeepResourcesOnPathChange;
                    case "Enhancement":
                        return true;
                    case "PrepareCache":
                        var uiOptions = serviceProvider.GetRequiredService<IBOptions<UIOptions>>();
                        return !uiOptions.Value.Resource.DisableCache;
                    case "MoveFiles":
                        var fsOptions = serviceProvider.GetRequiredService<IBOptions<FileSystemOptions>>();
                        return fsOptions.Value.FileMover?.Enabled ?? false;

                    default:
                        return true;
                }
            },
            GetInterval = () =>
            {
                // Can be made configurable in the future
                return x.Key switch
                {
                    _ => TimeSpan.FromMinutes(1)
                };
            },
            WatchedOptionsTypes = x.Key switch
            {
                "GenerateResourceMarker" => new[] { typeof(ResourceOptions) },
                // "Enhancement" => new[] { typeof(EnhancerOptions) },
                "PrepareCache" => new[] { typeof(ResourceOptions) },
                "MoveFiles" => new[] { typeof(FileSystemOptions) },
                _ => Array.Empty<Type>()
            },
            BuildHandler = _ => new BTaskHandlerBuilder
            {
                Type = BTaskType.Any,
                ResourceType = BTaskResourceType.Any,
                GetName = () => localizer.BTask_Name(x.Key),
                GetDescription = () => localizer.BTask_Description(x.Key),
                GetMessageOnInterruption = () => localizer.BTask_MessageOnInterruption(x.Key),
                CancellationToken = null,
                Id = x.Key,
                Run = async args =>
                {
                    await using var scope = serviceProvider.CreateAsyncScope();
                    var sp = scope.ServiceProvider;
                    await x.Value(args, sp);
                },
                ConflictKeys = [x.Key],
                Level = BTaskLevel.Default,
                Interval = TimeSpan.FromMinutes(1), // Will be overridden by GetInterval
                IsPersistent = true
            }
        }).ToArray();
    }

    public PredefinedTaskDefinition[] TaskDefinitions { get; }
}