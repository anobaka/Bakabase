using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/gui")]
    public class GuiController : Controller
    {
        private readonly IGuiAdapter _guiAdapter;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hubContext;

        public GuiController(IGuiAdapter guiAdapter, IHubContext<WebGuiHub, IWebGuiClient> hubContext)
        {
            _guiAdapter = guiAdapter;
            _hubContext = hubContext;
        }

        [HttpGet("url")]
        [SwaggerOperation(OperationId = "OpenUrlInDefaultBrowser")]
        public async Task<BaseResponse> OpenUrlInDefaultBrowser(string url)
        {
            Process.Start(new ProcessStartInfo(url) {UseShellExecute = true});
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("test-notification")]
        [SwaggerOperation(OperationId = "SendTestNotification")]
        public async Task<BaseResponse> SendTestNotification([FromBody] AppNotificationMessageViewModel notification)
        {
            await _hubContext.Clients.All.OnNotification(notification);
            return BaseResponseBuilder.Ok;
        }
    }
}