using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Gui.Extensions
{
    public static class GuiExtensions
    {
        public static IApplicationBuilder ConfigureGui(this IApplicationBuilder app)
        {
            var thirdPartyServices =
                app.ApplicationServices.GetRequiredService<IEnumerable<IDependentComponentService>>();
            var hubContext = app.ApplicationServices.GetRequiredService<IHubContext<WebGuiHub, IWebGuiClient>>();
            foreach (var service in thirdPartyServices)
            {
                service.OnStateChange += async (context) =>
                {
                    await hubContext.Clients.All.GetIncrementalData(nameof(DependentComponentContext),
                        service.BuildContextDto());
                };
            }

            var appUpdater = app.ApplicationServices.GetRequiredService<AppUpdater>();
            appUpdater.OnStateChange += async (context) =>
            {
                await hubContext.Clients.All.GetAppUpdaterState(appUpdater.State);
            };

            return app;
        }
    }
}