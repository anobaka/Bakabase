using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.Downloads;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    /// <summary>
    /// How to reach this library from a device that is not this one.
    /// </summary>
    [Route("~/other-devices")]
    public class OtherDevicesController(AppDownloadManifestService manifests) : Controller
    {
        private const string MobileManifestUrl =
            "https://cdn-public.anobaka.com/app/bakabase-mobile/manifest.json";

        /// <summary>
        /// The latest published packages for phones and tablets.
        /// </summary>
        /// <remarks>
        /// <see cref="OtherDeviceDownloadsViewModel.Mobile"/> is null when its manifest is
        /// unreachable and nothing is cached.
        /// </remarks>
        [HttpGet("downloads")]
        [SwaggerOperation(OperationId = "GetOtherDeviceDownloads")]
        [RemoteAccessible]
        public async Task<SingletonResponse<OtherDeviceDownloadsViewModel>> GetDownloads()
        {
            var ct = HttpContext.RequestAborted;

            return new SingletonResponse<OtherDeviceDownloadsViewModel>(
                new OtherDeviceDownloadsViewModel
                {
                    Mobile = await manifests.GetAsync<MobileAppDownloadsViewModel>(MobileManifestUrl, ct)
                });
        }
    }
}
