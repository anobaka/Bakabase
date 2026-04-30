using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.Service.Components
{
    /// <summary>
    /// Hub-backed implementation of <see cref="ILegacyInstallNotifier"/>. Lives in the Service
    /// project because that's where <c>WebGuiHub</c> lives.
    /// </summary>
    public sealed class HubLegacyInstallNotifier : ILegacyInstallNotifier
    {
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hub;

        public HubLegacyInstallNotifier(IHubContext<WebGuiHub, IWebGuiClient> hub)
        {
            _hub = hub;
        }

        public Task NotifyAsync(string legacyPath, CancellationToken cancellationToken)
        {
            return _hub.Clients.All.LegacyInstallAppDataDetected(new { path = legacyPath });
        }
    }
}
