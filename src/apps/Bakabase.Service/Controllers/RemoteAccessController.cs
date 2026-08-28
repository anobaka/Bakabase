using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    /// <summary>
    /// Pairing and status for devices other than the host machine.
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
        [RemoteAccessible(AllowAnonymous = true)]
        public SingletonResponse<RemoteAccessClientContextViewModel> GetContext()
        {
            var context = HttpContext.GetRemoteAccessContext();
            var mode = remoteAccessService.GetEffectiveMode();

            return new SingletonResponse<RemoteAccessClientContextViewModel>(
                new RemoteAccessClientContextViewModel
                {
                    IsLocal = context?.IsLoopback ?? true,
                    Mode = mode,
                    Paired = context?.Device != null,
                    DeviceId = context?.Device?.Id,
                    DeviceName = context?.Device?.Name
                });
        }

        /// <summary>
        /// Exchanges the pairing code shown on the host for a device token. The token
        /// is returned once and also set as a cookie, because the images, media
        /// elements and event streams the UI relies on cannot send headers.
        /// </summary>
        [HttpPost("pair")]
        [SwaggerOperation(OperationId = "PairRemoteDevice")]
        [RemoteAccessible(AllowAnonymous = true)]
        public async Task<SingletonResponse<RemoteDevicePairingViewModel>> Pair(
            [FromBody] RemoteDevicePairInputModel model)
        {
            var result = await remoteAccessService.PairAsync(model.PairingCode, model.DeviceName, model.Platform);
            if (result == null)
            {
                return SingletonResponseBuilder<RemoteDevicePairingViewModel>.Build(
                    ResponseCode.InvalidPayloadOrOperation, "The pairing code is wrong or has expired.");
            }

            Response.Cookies.Append(RemoteAccessHttpContextExtensions.DeviceTokenCookieName, result.Token,
                new CookieOptions
                {
                    HttpOnly = true,
                    // Bakabase runs over plain HTTP on the LAN; marking the cookie
                    // Secure would stop it being sent at all.
                    Secure = false,
                    SameSite = SameSiteMode.Lax,
                    // Long-lived on purpose: re-pairing a phone every session would be
                    // worse than the marginal risk, and the device can be revoked.
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    Path = "/"
                });

            return new SingletonResponse<RemoteDevicePairingViewModel>(new RemoteDevicePairingViewModel
            {
                Token = result.Token,
                DeviceId = result.Device.Id,
                DeviceName = result.Device.Name
            });
        }

        #region Host-side management

        /// <summary>
        /// The current mode plus the paired devices. Host-only: this is the page the
        /// user manages remote access from.
        /// </summary>
        [HttpGet("settings")]
        [SwaggerOperation(OperationId = "GetRemoteAccessSettings")]
        public SingletonResponse<RemoteAccessSettingsViewModel> GetSettings()
        {
            var pairingCode = remoteAccessService.GetPairingCode();

            return new SingletonResponse<RemoteAccessSettingsViewModel>(new RemoteAccessSettingsViewModel
            {
                Mode = remoteAccessService.GetEffectiveMode(),
                PairingCode = pairingCode?.Code,
                PairingCodeExpiresAt = pairingCode?.ExpiresAt,
                Devices = remoteAccessService.GetDevices().Select(d => new RemoteDeviceViewModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Platform = d.Platform,
                    CreatedAt = d.CreatedAt,
                    LastSeenAt = d.LastSeenAt
                }).ToList()
            });
        }

        [HttpPut("mode")]
        [SwaggerOperation(OperationId = "SetRemoteAccessMode")]
        public async Task<BaseResponse> SetMode([FromBody] RemoteAccessModeInputModel model)
        {
            await remoteAccessService.SetModeAsync(model.Mode);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("pairing-code")]
        [SwaggerOperation(OperationId = "IssueRemoteAccessPairingCode")]
        public async Task<SingletonResponse<RemoteAccessPairingCodeViewModel>> IssuePairingCode()
        {
            var info = await remoteAccessService.IssuePairingCodeAsync();
            return new SingletonResponse<RemoteAccessPairingCodeViewModel>(new RemoteAccessPairingCodeViewModel
            {
                Code = info.Code,
                ExpiresAt = info.ExpiresAt
            });
        }

        [HttpDelete("devices/{deviceId}")]
        [SwaggerOperation(OperationId = "RevokeRemoteDevice")]
        public async Task<BaseResponse> RevokeDevice(string deviceId)
        {
            await remoteAccessService.RevokeDeviceAsync(deviceId);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("devices/{deviceId}/name")]
        [SwaggerOperation(OperationId = "RenameRemoteDevice")]
        public async Task<BaseResponse> RenameDevice(string deviceId, [FromBody] RemoteDeviceRenameInputModel model)
        {
            await remoteAccessService.RenameDeviceAsync(deviceId, model.Name);
            return BaseResponseBuilder.Ok;
        }

        #endregion
    }
}
