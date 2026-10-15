using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed class MetadataSyncTask(IServiceProvider services, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(services, localizer)
{
    public override string Id => "MetadataSync";
    public override bool IsEnabled() => true;
    public override TimeSpan? GetInterval() => TimeSpan.FromSeconds(15);
    public override Task RunAsync(BTaskArgs args) =>
        ServiceProvider.GetRequiredService<MetadataReplicaCoordinator>().RunCycleAsync(
            (name, count) => args.UpdateTask(task => task.SetProcess(BTaskText.Localize(Localizer,
                "BTask_Process_MetadataSync_Resources", name, count))), args.YieldAsync, args.CancellationToken);
}
