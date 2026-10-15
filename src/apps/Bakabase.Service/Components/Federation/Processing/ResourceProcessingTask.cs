using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation.Processing;

public sealed class ResourceProcessingTask(IServiceProvider services, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(services, localizer)
{
    public override string Id => "ResourceProcessing";
    public override bool IsEnabled() => true;
    public override TimeSpan? GetInterval() => TimeSpan.FromSeconds(2);
    public override Task RunAsync(BTaskArgs args) => ServiceProvider.GetRequiredService<ResourceProcessingCoordinator>()
        .RunCycleAsync(job => args.UpdateTask(t => t.SetProcess(BTaskText.Localize(Localizer,
            "BTask_Process_ResourceProcessing_Resources", job.ProcessedCount))), args.YieldAsync, args.CancellationToken);
}
