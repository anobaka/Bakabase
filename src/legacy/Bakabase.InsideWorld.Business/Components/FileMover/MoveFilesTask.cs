using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.FileMover;

public class MoveFilesTask : AbstractPredefinedBTaskBuilder
{
    public MoveFilesTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "MoveFiles";

    public override bool IsEnabled()
    {
        var fsOptions = ServiceProvider.GetRequiredService<IBOptions<FileSystemOptions>>();
        return fsOptions.Value.FileMover?.Enabled ?? false;
    }

    public override Type[] WatchedOptionsTypes => [typeof(FileSystemOptions)];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileMover>();
        await service.MovingFiles(
            async p => await args.UpdateTask(t => t.Percentage = p),
            args.PauseToken,
            args.CancellationToken);
    }
}
