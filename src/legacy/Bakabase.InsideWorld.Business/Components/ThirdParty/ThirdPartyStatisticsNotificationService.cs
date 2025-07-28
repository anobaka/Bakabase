using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.ThirdParty.Services;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty;

public class ThirdPartyStatisticsNotificationService(IHubContext<WebGuiHub, IWebGuiClient> hubContext)
    : IThirdPartyStatisticsNotificationService
{
    public async Task NotifyStatisticsChanged(ThirdPartyRequestStatistics[] statistics)
    {
        await hubContext.Clients.All.UpdateThirdPartyRequestStatistics(statistics);
    }
}