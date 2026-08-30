using System.Threading.Tasks;
using Bakabase.Service.Components.Mobile;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    /// <summary>
    /// Where to get the mobile companion app.
    /// </summary>
    [Route("~/mobile-app")]
    public class MobileAppController(MobileAppDownloadService downloadService) : Controller
    {
        /// <summary>
        /// The latest published mobile packages. Null data when the download
        /// manifest is unreachable and nothing is cached (offline host, or no
        /// mobile release published yet).
        /// </summary>
        [HttpGet("downloads")]
        [SwaggerOperation(OperationId = "GetMobileAppDownloads")]
        [RemoteAccessible]
        public async Task<SingletonResponse<MobileAppDownloadsViewModel>> GetDownloads()
        {
            return new SingletonResponse<MobileAppDownloadsViewModel>(
                await downloadService.GetAsync(HttpContext.RequestAborted));
        }
    }
}
