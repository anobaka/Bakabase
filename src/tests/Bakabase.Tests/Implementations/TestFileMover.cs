using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests.Implementations;

public class TestFileMover : FileMover
{
    public TestFileMover(AspNetCoreOptionsManager<FileSystemOptions> optionsManager,
        BackgroundTaskManager backgroundTaskManager, ILogger<FileMover> logger,
        IHubContext<WebGuiHub, IWebGuiClient> uiHub) : base(optionsManager, backgroundTaskManager, logger, uiHub)
    {
    }

    public async Task TestMoving(FileSystemOptions.FileMoverOptions options)
    {
        await MoveInternal(options, null, new CancellationToken());
    }

    public void SetCreationTime(string path, DateTime dt)
    {
        CreationTimeCache[path.StandardizePath()!] = dt;
    }
}