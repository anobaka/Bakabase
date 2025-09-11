using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

public class BTaskEventHandler(IHubContext<WebGuiHub, IWebGuiClient> uiHub, IGuiAdapter guiAdapter, ILogger<BTaskEventHandler> logger) : IBTaskEventHandler
{
    public async Task OnTaskChange(BTaskViewModel task)
    {
        // logger.LogInformation($"BTask status changed: [{task.Id}]{task.Name} -> {task.Status}", task);
        await uiHub.Clients.All.GetIncrementalData("BTask", task);
    }

    public async Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks)
    {
        await uiHub.Clients.All.GetData("BTask", tasks);
    }

    public async Task OnTaskManagerStatusChange(bool isRunning)
    {
        var filename = isRunning ? "tray-running" : "favicon";
        var icon = $"Assets/{filename}.ico";
        guiAdapter.SetTrayIcon(new Icon(icon));
    }
}