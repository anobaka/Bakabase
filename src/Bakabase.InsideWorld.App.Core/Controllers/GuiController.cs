using System.Diagnostics;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/gui")]
    public class GuiController : Controller
    {
        private readonly IGuiAdapter _guiAdapter;

        public GuiController(IGuiAdapter guiAdapter)
        {
            _guiAdapter = guiAdapter;
        }

        [HttpGet("files-selector")]
        [SwaggerOperation(OperationId = "OpenFilesSelector")]
        public async Task<ListResponse<string>> OpenFilesSelector(string? initialDirectory)
        {
            return new ListResponse<string>(_guiAdapter.OpenFilesSelector(initialDirectory));

        }

        [HttpGet("file-selector")]
        [SwaggerOperation(OperationId = "OpenFileSelector")]
        public async Task<SingletonResponse<string>> OpenFileSelector(string? initialDirectory)
        {
            return new SingletonResponse<string>(_guiAdapter.OpenFileSelector(initialDirectory));

        }

        [HttpGet("folder-selector")]
        [SwaggerOperation(OperationId = "OpenFolderSelector")]
        public async Task<SingletonResponse<string>> OpenFolderSelector(string? initialDirectory)
        {
            return new SingletonResponse<string>(_guiAdapter.OpenFolderSelector(initialDirectory));
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