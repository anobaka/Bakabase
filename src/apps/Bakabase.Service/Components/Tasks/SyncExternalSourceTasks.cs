using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Tasks;

public class SyncSteamTask : AbstractPredefinedBTaskBuilder
{
    public SyncSteamTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SyncSteam";

    public override bool IsEnabled()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<SteamOptions>>();
        return options.Value.AutoSyncIntervalMinutes is > 0;
    }

    public override TimeSpan? GetInterval()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<SteamOptions>>();
        var minutes = options.Value.AutoSyncIntervalMinutes;
        return minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    public override Type[] WatchedOptionsTypes => [typeof(SteamOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISteamAppService>();
        await svc.SyncFromApi(
            async (percentage, count) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = count.ToString();
                });
            },
            args.CancellationToken);

        var pathMarkSyncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
        await pathMarkSyncService.EnqueueSync(ResourceSource.Steam);
    }
}

public class SyncDLsiteTask : AbstractPredefinedBTaskBuilder
{
    public SyncDLsiteTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SyncDLsite";

    public override bool IsEnabled()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<DLsiteOptions>>();
        return options.Value.AutoSyncIntervalMinutes is > 0;
    }

    public override TimeSpan? GetInterval()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<DLsiteOptions>>();
        var minutes = options.Value.AutoSyncIntervalMinutes;
        return minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    public override Type[] WatchedOptionsTypes => [typeof(DLsiteOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDLsiteWorkService>();
        await svc.SyncFromApi(
            async (percentage, count) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = count.ToString();
                });
            },
            args.CancellationToken);

        var pathMarkSyncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
        await pathMarkSyncService.EnqueueSync(ResourceSource.DLsite);
    }
}

public class SyncExHentaiTask : AbstractPredefinedBTaskBuilder
{
    public SyncExHentaiTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SyncExHentai";

    public override bool IsEnabled()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<ExHentaiOptions>>();
        return options.Value.AutoSyncIntervalMinutes is > 0;
    }

    public override TimeSpan? GetInterval()
    {
        var options = ServiceProvider.GetRequiredService<IBOptions<ExHentaiOptions>>();
        var minutes = options.Value.AutoSyncIntervalMinutes;
        return minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
    }

    public override Type[] WatchedOptionsTypes => [typeof(ExHentaiOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExHentaiGalleryService>();
        await svc.SyncFromApi(
            async (percentage, count) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = count.ToString();
                });
            },
            args.CancellationToken);

        var pathMarkSyncService = scope.ServiceProvider.GetRequiredService<IPathMarkSyncService>();
        await pathMarkSyncService.EnqueueSync(ResourceSource.ExHentai);
    }
}
