using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Search.Index;

public class SearchIndexTask : AbstractPredefinedBTaskBuilder
{
    public SearchIndexTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SearchIndex";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => null; // One-time task at startup

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ResourceSearchIndexService>();
        await indexService.RebuildAllAsync(
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
