using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Enhancer;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

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
                    await service.EnhanceAll(async p => await args.UpdateTask(t => t.Percentage = p), args.PauseToken,
                        args.CancellationToken);
                }
            },
            {
                "PrepareCache", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IResourceService>();
                    await service.PrepareCache(async p => await args.UpdateTask(t => t.Percentage = p), args.PauseToken,
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
        };

        DescriptorBuilders = simpleTaskBuilders.Select(x => new BTaskHandlerBuilder
        {
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