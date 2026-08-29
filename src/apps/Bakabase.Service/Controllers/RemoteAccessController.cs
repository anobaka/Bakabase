using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    /// <summary>
    /// Status and settings for using Bakabase from a device other than the host.
    /// </summary>
    [Route("~/remote-access")]
    public class RemoteAccessController(IRemoteAccessService remoteAccessService) : Controller
    {
        /// <summary>
        /// Tells a client which side of the gate it is on. Called by the SPA at
        /// startup so it knows whether to offer host-only actions (launching a player,
        /// opening a folder) or route playback into the browser instead.
        /// </summary>
        [HttpGet("context")]
        [SwaggerOperation(OperationId = "GetRemoteAccessContext")]
        [RemoteAccessible]
        public SingletonResponse<RemoteAccessClientContextViewModel> GetContext()
        {
            var context = HttpContext.GetRemoteAccessContext();

            return new SingletonResponse<RemoteAccessClientContextViewModel>(
                new RemoteAccessClientContextViewModel
                {
                    IsLocal = context?.IsLoopback ?? true,
                    Mode = remoteAccessService.GetEffectiveMode()
                });
        }

        /// <summary>
        /// The current mode plus the addresses another device can open. Host-only:
        /// this is the page remote access is configured from.
        /// </summary>
        [HttpGet("settings")]
        [SwaggerOperation(OperationId = "GetRemoteAccessSettings")]
        public SingletonResponse<RemoteAccessSettingsViewModel> GetSettings()
        {
            return new SingletonResponse<RemoteAccessSettingsViewModel>(new RemoteAccessSettingsViewModel
            {
                Mode = remoteAccessService.GetEffectiveMode(),
                Addresses = remoteAccessService.GetReachableAddresses()
                    .Select(a => new RemoteAccessAddressViewModel {Url = a.Url, InterfaceName = a.InterfaceName})
                    .ToList()
            });
        }

        [HttpPut("mode")]
        [SwaggerOperation(OperationId = "SetRemoteAccessMode")]
        public async Task<BaseResponse> SetMode([FromBody] RemoteAccessModeInputModel model)
        {
            await remoteAccessService.SetModeAsync(model.Mode);
            return BaseResponseBuilder.Ok;
        }
    }
}
