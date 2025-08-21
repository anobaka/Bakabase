using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/gui")]
    public class GuiController : Controller
    {
        private readonly IGuiAdapter _guiAdapter;

        public GuiController(IGuiAdapter guiAdapter)
        {
            _guiAdapter = guiAdapter;
        }

        [HttpGet("url")]
        [SwaggerOperation(OperationId = "OpenUrlInDefaultBrowser")]
        public async Task<BaseResponse> OpenUrlInDefaultBrowser(string url)
        {
            Process.Start(new ProcessStartInfo(url) {UseShellExecute = true});
            return BaseResponseBuilder.Ok;
        }
    }
}