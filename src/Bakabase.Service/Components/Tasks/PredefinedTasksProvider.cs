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
            }
            ,
            {
                "KeepResourcesOnPathChange", async (args, sp) =>
                {
                    var resourceService = sp.GetRequiredService<IResourceService>();
                    var resources = await resourceService.GetAll(r => true);
                    var folderResources = resources.Where(r => !r.IsFile).ToList();
                    int processed = 0;
                    foreach (var r in folderResources)
                    {
                        args.CancellationToken?.ThrowIfCancellationRequested();

                        var dir = r.Path;
                        try
                        {
                            if (Directory.Exists(dir))
                            {
                                var marker = Path.Combine(dir, InternalOptions.ResourceMarkerFileName);
                                var content = JsonSerializer.Serialize(new { id = r.Id });
                                await File.WriteAllTextAsync(marker, content);
                                File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.Hidden);
                            }
                        }
                        catch
                        {
                            // ignore per-folder errors
                        }

                        processed++;
                        var percent = (int)(processed * 100f / folderResources.Count);
                        await args.UpdateTask(t => t.Percentage = percent);
                    }
                }
            }
        };

        DescriptorBuilders = simpleTaskBuilders.Select(x => new BTaskHandlerBuilder
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
            Interval = TimeSpan.FromMinutes(1),
            IsPersistent = true
        }).ToArray();
    }

    public BTaskHandlerBuilder[] DescriptorBuilders { get; }
}