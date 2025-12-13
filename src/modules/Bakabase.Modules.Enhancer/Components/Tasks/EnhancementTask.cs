using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Enhancer.Components.Tasks;

public class EnhancementTask : AbstractPredefinedBTaskBuilder
{
    public EnhancementTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "Enhancement";

    public override bool IsEnabled() => true;

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEnhancerService>();
        await service.EnhanceAll(
            async p => await args.UpdateTask(t => t.Percentage = p),
            async p => await args.UpdateTask(t => t.Process = p),
            args.PauseToken,
            args.CancellationToken);
    }
}
