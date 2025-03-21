using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
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
                    await service.EnhanceAll(args.OnPercentageChange, args.PauseToken, args.CancellationToken);
                }
            },
            {
                "PrepareCache", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IResourceService>();
                    await service.PrepareCache(args.OnPercentageChange, args.PauseToken, args.CancellationToken);
                }
            },
            {
                "MoveFiles", async (args, sp) =>
                {
                    var service = sp.GetRequiredService<IFileMover>();
                    await service.MovingFiles(args.OnPercentageChange, args.PauseToken, args.CancellationToken);
                }
            }
        };

        DescriptorBuilders = simpleTaskBuilders.Select(x => new BTaskDescriptorBuilder
        {
            GetName = () => localizer.BTask_Name(x.Key),
            GetDescription = () => localizer.BTask_Description(x.Key),
            GetMessageOnInterruption = () => localizer.BTask_MessageOnInterruption(x.Key),
            OnProcessChange = null,
            OnPercentageChange = null,
            OnStatusChange = null,
            CancellationToken = null,
            Id = x.Key,
            Run = async args =>
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                await x.Value(args, sp);
            },
            Args = null,
            ConflictKeys = [x.Key],
            Level = BTaskLevel.Default,
            Interval = TimeSpan.FromMinutes(1)
        }).ToArray();
    }

    public BTaskDescriptorBuilder[] DescriptorBuilders { get; }
}