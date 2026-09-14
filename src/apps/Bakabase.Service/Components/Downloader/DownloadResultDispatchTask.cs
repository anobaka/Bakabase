using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Downloader;

public sealed class DownloadResultDispatchTask(IServiceProvider services, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(services, localizer)
{
    public override string Id => "DownloadResultDispatch";
    public override bool IsEnabled() => true;
    public override TimeSpan? GetInterval() => TimeSpan.FromSeconds(10);
    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DownloadResultWorkflowService>().DispatchAsync(args.CancellationToken);
    }
}
