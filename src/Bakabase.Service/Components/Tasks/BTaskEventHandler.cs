using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.Service.Components.Tasks;

public class BTaskEventHandler(IHubContext<WebGuiHub, IWebGuiClient> uiHub) : IBTaskEventHandler
{
    public async Task OnTaskChange(BTaskViewModel task)
    {
        await uiHub.Clients.All.GetIncrementalData("BTask", task);
    }

    public async Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks)
    {
        await uiHub.Clients.All.GetData("BTask", tasks);
    }
}