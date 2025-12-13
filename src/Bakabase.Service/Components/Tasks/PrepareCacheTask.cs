using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Tasks;

public class PrepareCacheTask : AbstractPredefinedBTaskBuilder
{
    public PrepareCacheTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "PrepareCache";

    public override bool IsEnabled()
    {
        var uiOptions = ServiceProvider.GetRequiredService<IBOptions<UIOptions>>();
        return !uiOptions.Value.Resource.DisableCache;
    }

    public override Type[] WatchedOptionsTypes => [typeof(UIOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResourceService>();
        await service.PrepareCache(
            async p => await args.UpdateTask(t => t.Percentage = p),
            async p => await args.UpdateTask(t => t.Process = p),
            args.PauseToken,
            args.CancellationToken);
    }
}
