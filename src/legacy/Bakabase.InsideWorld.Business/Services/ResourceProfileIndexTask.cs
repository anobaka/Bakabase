using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceProfileIndexTask : AbstractPredefinedBTaskBuilder
{
    public ResourceProfileIndexTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "ResourceProfileIndex";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => null; // One-time task at startup

    public override HashSet<string>? DependsOn => ["SearchIndex"]; // Must wait for SearchIndex to complete first

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<IResourceProfileIndexService>();
        await indexService.RebuildAsync(
            async (percentage, process) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = process;
                });
            },
            args.CancellationToken);
    }
}
