using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    /// <summary>
    /// Status and settings for using Bakabase from a device other than the host.
    /// </summary>
    /// <remarks>
    /// The pairing endpoints split into two groups, and the split is what keeps them
    /// safe. Everything under <c>pair/</c> is reachable without credentials — a device
    /// with no key has to be able to get one — and each of those carries its secret in
    /// the body rather than the URL. Everything under <c>pairing/</c> and
    /// <c>devices/</c> is management, so it is left unmarked and only the host or an
    /// already-paired device reaches it — a paired device through its pairing, which
    /// the gate lets reach everything. Marking one <see cref="RemoteAccessibleAttribute"/>
    /// would add nobody a paired device needs; it would add the unpaired caller of an
    /// Enabled server that does not require pairing, which could then approve its own
    /// request and hold a device key. In Unrestricted mode every caller reaches them:
    /// there the LAN browser is the operator, and it is where a headless server's
    /// requests are answered and its codes issued.
    /// </remarks>
    [Route("~/remote-access")]
    public class RemoteAccessController(
        IRemoteAccessService remoteAccessService,
        IRemoteDeviceService deviceService,
        RemoteConnectionRegistry connections,
        INotificationService notificationService,
        PairingRequestRateLimiter rateLimiter) : Controller
    {
        /// <summary>
        /// Recorded as the approver when the approval came from the host itself rather
        /// than from another device. Distinct from null, which means "paired with a
        /// code, nobody approved it".
        /// </summary>
        private const string HostApproverId = "host";

        /// <summary>
        /// Tells a client which side of the gate it is on. Called by the SPA at
        /// startup so it knows whether to offer host-only actions (launching a player,
        /// opening a folder) or route playback into the browser instead.
        /// </summary>
        [HttpGet("context")]
        [SwaggerOperation(OperationId = "GetRemoteAccessContext")]
        [RemoteAccessible]
        public async Task<SingletonResponse<RemoteAccessClientContextViewModel>> GetContext()
        {
            var context = HttpContext.GetRemoteAccessContext();
            var isLocal = context?.IsLoopback ?? true;
            var descriptor = await remoteAccessService.GetServerDescriptorAsync();

            return new SingletonResponse<RemoteAccessClientContextViewModel>(
                new RemoteAccessClientContextViewModel
                {
                    IsLocal = isLocal,
                    Mode = remoteAccessService.GetEffectiveMode(),
                    Paired = context?.IsPaired ?? false,
                    DeviceId = context?.Device?.Id,
                    DeviceName = context?.Device?.Name,
                    // A caller reaching a server directly is either sitting at it or
                    // browsing it. The third answer only ever comes from a client that
                    // answers this endpoint itself.
                    ClientMode = isLocal ? ClientMode.AllInOne : ClientMode.RemoteBrowser,
                    ServerId = descriptor.Id,
                    ServerName = descriptor.Name,
                    // Needs a desktop, and needs it to be this person's. A container has
                    // no screen, and a browser on another device is not sitting here.
                    CookieCaptureAvailable = isLocal && AppService.RuntimeMode != RuntimeMode.Docker
                });
        }

        /// <summary>
        /// Who this server is — same facts as the discovery beacon broadcasts, so a
        /// client that typed an address by hand still learns the install's identity,
        /// name and protocol version before talking further.
        /// </summary>
        [HttpGet("server-info")]
        [SwaggerOperation(OperationId = "GetRemoteAccessServerInfo")]
        [RemoteAccessible]
        public async Task<SingletonResponse<RemoteAccessServerInfoViewModel>> GetServerInfo()
        {
            var descriptor = await remoteAccessService.GetServerDescriptorAsync();

            return new SingletonResponse<RemoteAccessServerInfoViewModel>(new RemoteAccessServerInfoViewModel
            {
                Id = descriptor.Id,
                Name = descriptor.Name,
                AppVersion = descriptor.AppVersion,
                ProtocolVersion = descriptor.ProtocolVersion,
                Mode = remoteAccessService.GetEffectiveMode(),
                PairingSupported = true,
                ServerTime = DateTime.UtcNow,
                Kind = descriptor.Kind,
                Platform = descriptor.Platform
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
            var code = deviceService.GetPairingCodeStatus();

            return new SingletonResponse<RemoteAccessSettingsViewModel>(new RemoteAccessSettingsViewModel
            {
                Mode = remoteAccessService.GetEffectiveMode(),
                Addresses = remoteAccessService.GetReachableAddresses()
                    .Select(a => new RemoteAccessAddressViewModel {Url = a.Url, InterfaceName = a.InterfaceName})
                    .ToList(),
                AllowLiveTranscode = remoteAccessService.GetAllowLiveTranscode(),
                RequirePairing = remoteAccessService.GetRequirePairing(),
                Devices = deviceService.GetDevices().Select(ToViewModel).ToList(),
                PendingRequests = deviceService.GetPendingRequests().Select(ToViewModel).ToList(),
                PairingCode = code == null
                    ? null
                    : new RemoteAccessPairingCodeViewModel
                    {
                        ExpiresAt = code.ExpiresAt,
                        RemainingAttempts = code.RemainingAttempts
                    }
            });
        }

        [HttpPut("mode")]
        [SwaggerOperation(OperationId = "SetRemoteAccessMode")]
        public async Task<BaseResponse> SetMode([FromBody] RemoteAccessModeInputModel model)
        {
            await remoteAccessService.SetModeAsync(model.Mode);

            // Every other check is per-request and takes effect on the next call. A hub
            // connection is authorized once at its handshake, so switching remote access
            // off has to reach the ones already open or they keep receiving pushes.
            if (remoteAccessService.GetEffectiveMode() == RemoteAccessMode.Disabled)
            {
                connections.AbortAll();
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpPut("live-transcode")]
        [SwaggerOperation(OperationId = "SetRemoteAccessLiveTranscode")]
        public async Task<BaseResponse> SetLiveTranscode([FromBody] RemoteAccessLiveTranscodeInputModel model)
        {
            await remoteAccessService.SetAllowLiveTranscodeAsync(model.Allow);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("require-pairing")]
        [SwaggerOperation(OperationId = "SetRemoteAccessRequirePairing")]
        public async Task<BaseResponse> SetRequirePairing([FromBody] RemoteAccessRequirePairingInputModel model)
        {
            await remoteAccessService.SetRequirePairingAsync(model.Require);

            // Leaving unpaired hub connections open would keep serving exactly the
            // callers this switch was flipped to shut out.
            if (model.Require)
            {
                connections.AbortUnpaired();
            }

            return BaseResponseBuilder.Ok;
        }

        #region Pairing: what a device with no credentials may call

        /// <summary>
        /// Exchanges a pairing code for credentials. The code is spent on success and
        /// counted against on failure, so guessing runs out.
        /// </summary>
        [HttpPost("pair/code")]
        [SwaggerOperation(OperationId = "PairRemoteDeviceWithCode")]
        [RemoteAccessible]
        public async Task<SingletonResponse<RemoteAccessPairingResultViewModel>> PairWithCode(
            [FromBody] RemoteAccessPairWithCodeInputModel model)
        {
            var result = await deviceService.PairWithCodeAsync(model.Code, model.DeviceName ?? string.Empty,
                model.Platform, HttpContext.RequestAborted);

            return new SingletonResponse<RemoteAccessPairingResultViewModel>(await ToViewModelAsync(result));
        }

        /// <summary>
        /// Asks an already-paired device to let this one in, for when nobody can read
        /// the host's screen or logs to fetch a code.
        /// </summary>
        [HttpPost("pair/request")]
        [SwaggerOperation(OperationId = "RequestRemoteDevicePairing")]
        [RemoteAccessible]
        public async Task<SingletonResponse<RemoteAccessPairingRequestAcceptedViewModel>> RequestPairing(
            [FromBody] RemoteAccessPairRequestInputModel model, [FromServices] IBakabaseLocalizer localizer)
        {
            var remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // This is the one endpoint an uncredentialed caller can make write to disk,
            // so its budget is checked before anything happens rather than after.
            if (!rateLimiter.TryTake(remoteAddress))
            {
                return new SingletonResponse<RemoteAccessPairingRequestAcceptedViewModel>(
                    new RemoteAccessPairingRequestAcceptedViewModel {Failure = PairingFailure.TooManyAttempts});
            }

            var request = await deviceService.RequestPairingAsync(model.DeviceName ?? string.Empty, model.Platform,
                remoteAddress, HttpContext.RequestAborted);

            await AnnounceManagementRequestAsync(request, localizer);

            return new SingletonResponse<RemoteAccessPairingRequestAcceptedViewModel>(
                new RemoteAccessPairingRequestAcceptedViewModel
                {
                    RequestId = request.Id,
                    ExpiresAt = request.ExpiresAt
                });
        }

        /// <summary>
        /// Collects the credentials an approval produced. The waiting device polls this;
        /// until somebody approves it answers <see cref="PairingFailure.NotYetApproved"/>
        /// rather than an error, because waiting is the normal state here.
        /// </summary>
        [HttpPost("pair/claim")]
        [SwaggerOperation(OperationId = "ClaimRemoteDevicePairing")]
        [RemoteAccessible]
        public async Task<SingletonResponse<RemoteAccessPairingResultViewModel>> ClaimPairing(
            [FromBody] RemoteAccessPairClaimInputModel model)
        {
            var result = await deviceService.ClaimApprovedAsync(model.RequestId ?? string.Empty,
                HttpContext.RequestAborted);

            return new SingletonResponse<RemoteAccessPairingResultViewModel>(await ToViewModelAsync(result));
        }

        #endregion

        #region Pairing: management, for the host and already-paired devices

        /// <summary>
        /// Issues a code that pairs whoever types it, and returns it in plain text. This
        /// is the only response that ever carries one; the settings page can afterwards
        /// see that a code exists and when it lapses, but not what it is.
        /// </summary>
        /// <remarks>
        /// Never for an unpaired caller of an Enabled server. A code lets in a device
        /// nobody has looked at — it is bearer access, and a phone that could mint one
        /// could pair anything without the approval step ever happening.
        /// </remarks>
        [HttpPost("pairing/code")]
        [SwaggerOperation(OperationId = "IssueRemoteAccessPairingCode")]
        public async Task<SingletonResponse<RemoteAccessIssuedPairingCodeViewModel>> IssuePairingCode()
        {
            var issue = await deviceService.IssuePairingCodeAsync(ct: HttpContext.RequestAborted);

            return new SingletonResponse<RemoteAccessIssuedPairingCodeViewModel>(
                new RemoteAccessIssuedPairingCodeViewModel {Code = issue.Code, ExpiresAt = issue.ExpiresAt});
        }

        /// <summary>
        /// Lets a device in. Callable from any device that is already paired, which is
        /// the decision that makes a headless server usable at all: nobody can walk over
        /// to a container and click a button, and the alternative — a code read out of
        /// the server's log — is a worse thing to ask of somebody every time.
        /// </summary>
        /// <remarks>
        /// Not <see cref="RemoteAccessibleAttribute"/>: an unpaired caller of an Enabled
        /// server that does not require pairing would otherwise approve the request it
        /// filed itself and collect a key with full control (see the class remarks).
        /// Answers the id the device will be listed under once it has collected its key, so
        /// whoever approved it can find it there. The key itself only ever goes to the device.
        /// </remarks>
        [HttpPost("pairing/requests/{id}/approve")]
        [SwaggerOperation(OperationId = "ApproveRemoteDevicePairingRequest")]
        public async Task<SingletonResponse<RemoteAccessPairingApprovalViewModel>> ApprovePairingRequest(string id)
        {
            var approver = HttpContext.GetRemoteAccessContext()?.Device?.Id ?? HostApproverId;
            var deviceId = await deviceService.ApproveRequestAsync(id, approver, HttpContext.RequestAborted);

            return deviceId != null
                ? new SingletonResponse<RemoteAccessPairingApprovalViewModel>(
                    new RemoteAccessPairingApprovalViewModel {DeviceId = deviceId})
                : SingletonResponseBuilder<RemoteAccessPairingApprovalViewModel>.Build(ResponseCode.NotFound,
                    "This pairing request has expired or was already handled.");
        }

        [HttpPost("pairing/requests/{id}/reject")]
        [SwaggerOperation(OperationId = "RejectRemoteDevicePairingRequest")]
        public async Task<BaseResponse> RejectPairingRequest(string id)
        {
            await deviceService.RejectRequestAsync(id, HttpContext.RequestAborted);

            // Rejecting something that is already gone is the outcome the caller wanted.
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// Every device that can reach this server, and what is waiting to.
        /// </summary>
        /// <remarks>
        /// Readable from any paired device, because a device that may approve another
        /// has to be able to see what it is approving and what it let in previously.
        /// Carries no keys — those exist on the server only to verify signatures.
        /// </remarks>
        [HttpGet("devices")]
        [SwaggerOperation(OperationId = "GetRemoteAccessDevices")]
        public ListResponse<RemoteAccessDeviceViewModel> GetDevices()
        {
            return new ListResponse<RemoteAccessDeviceViewModel>(
                deviceService.GetDevices().Select(ToViewModel).ToList());
        }

        /// <summary>
        /// Devices waiting to be let in.
        /// </summary>
        /// <remarks>
        /// Its own route rather than the settings page's copy of the same list: that one
        /// also carries the server's reachable addresses and its mode, which are the host's
        /// business and nobody else's.
        /// </remarks>
        [HttpGet("pairing/requests")]
        [SwaggerOperation(OperationId = "GetRemoteAccessPairingRequests")]
        public ListResponse<RemoteAccessPendingRequestViewModel> GetPendingRequests()
        {
            return new ListResponse<RemoteAccessPendingRequestViewModel>(
                deviceService.GetPendingRequests().Select(ToViewModel).ToList());
        }

        /// <summary>
        /// Takes a device's access away.
        /// </summary>
        /// <remarks>
        /// Also from another paired device, and deliberately including the caller itself:
        /// somebody whose phone is in a stranger's hands needs to be able to cut it off
        /// from whatever device they still have, and a lost phone is exactly the case
        /// where the host machine is not the one to hand.
        /// </remarks>
        [HttpDelete("devices/{id}")]
        [SwaggerOperation(OperationId = "RevokeRemoteAccessDevice")]
        public async Task<BaseResponse> RevokeDevice(string id)
        {
            await deviceService.RevokeAsync(id, HttpContext.RequestAborted);

            // The device's next HTTP call fails on its own — the authenticator looks it
            // up every time — but a hub connection it already holds would outlive the
            // revocation.
            connections.AbortDevice(id);

            return BaseResponseBuilder.Ok;
        }

        [HttpPut("devices/{id}/name")]
        [SwaggerOperation(OperationId = "RenameRemoteAccessDevice")]
        public async Task<BaseResponse> RenameDevice(string id, [FromBody] RemoteAccessDeviceNameInputModel model)
        {
            var renamed = await deviceService.RenameAsync(id, model.Name ?? string.Empty, HttpContext.RequestAborted);

            return renamed
                ? BaseResponseBuilder.Ok
                : BaseResponseBuilder.Build(ResponseCode.NotFound, "This device is no longer paired.");
        }

        #endregion

        /// <summary>
        /// Where a management request is approved — the devices page, in the section that
        /// lets other devices manage this one.
        /// </summary>
        internal const string ManagementRequestRoute = "/federation/devices?section=management";

        /// <summary>
        /// Tells whoever is at this server that a device asks to manage it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pairing grants full control — the same access the desktop app uses to switch its
        /// window to this server — so the notification says so, and links to the page where
        /// it is approved. The request expires in minutes, and whoever can approve it is
        /// unlikely to be on that page; a persistent notification reaches them wherever they
        /// are, and survives a reload the way a toast would not.
        /// </para>
        /// <para>
        /// Two limits keep it from becoming noise, as with the federation pairing
        /// notification. A device that files again while an earlier request of its own is
        /// still waiting and was announced — someone clicking "add" twice, an app restarted
        /// mid-wait — has already reached whoever approves, so it stays quiet. Only an
        /// announced request counts: one the throttle held back, or whose notification
        /// could not be created, told nobody, so the device's next request is announced.
        /// And the global throttle is separate from the per-address budget: many addresses
        /// can each stay inside theirs and still add up to a wall of notifications. The
        /// notification is a convenience; failing to raise one never fails the request,
        /// which is stored.
        /// </para>
        /// <para>
        /// The body names Configuration → Remote access, which lists waiting requests
        /// with approve and reject for every viewer who can see the notification — this
        /// machine, a paired device, the desktop app showing this server, a browser on an
        /// Unrestricted server. The link still opens the devices page's management section.
        /// </para>
        /// </remarks>
        private async Task AnnounceManagementRequestAsync(PendingPairingRequest request, IBakabaseLocalizer localizer)
        {
            var repeat = deviceService.GetPendingRequests().Any(r =>
                !string.Equals(r.Id, request.Id, StringComparison.Ordinal) &&
                string.Equals(r.DeviceName, request.DeviceName, StringComparison.Ordinal) &&
                r.Platform == request.Platform &&
                string.Equals(r.RemoteAddress, request.RemoteAddress, StringComparison.Ordinal) &&
                rateLimiter.WasAnnounced(r.Id));

            if (repeat || !rateLimiter.TryNotify())
            {
                return;
            }

            try
            {
                await notificationService.CreateAsync(new NotificationCreationInputModel
                {
                    Source = "RemoteAccess",
                    Title = localizer["RemoteAccess_ManagementRequest_Title", request.DeviceName],
                    Body = localizer["RemoteAccess_ManagementRequest_Body", request.Platform.ToString(),
                        request.RemoteAddress ?? "?"],
                    PayloadJson = JsonSerializer.Serialize(new {route = ManagementRequestRoute}),
                    Severity = AppNotificationSeverity.Warning
                });

                rateLimiter.MarkAnnounced(request.Id, request.ExpiresAt);
            }
            catch (Exception)
            {
                // Stored either way, and listed where it is approved.
            }
        }

        private async Task<RemoteAccessPairingResultViewModel> ToViewModelAsync(PairingResult result)
        {
            if (result.Credentials == null)
            {
                return new RemoteAccessPairingResultViewModel {Failure = result.Failure};
            }

            return new RemoteAccessPairingResultViewModel
            {
                Credentials = new RemoteAccessPairingCredentialsViewModel
                {
                    DeviceId = result.Credentials.DeviceId,
                    Key = result.Credentials.Key,
                    // Resolved here rather than in the service so the device is told the
                    // same identity discovery and server-info report, and can refuse to
                    // reuse credentials against a different install.
                    ServerId = await remoteAccessService.GetOrCreateServerIdAsync()
                }
            };
        }

        private static RemoteAccessDeviceViewModel ToViewModel(RemoteDevice device) =>
            new()
            {
                Id = device.Id,
                Name = device.Name,
                Platform = device.Platform,
                CreatedAt = device.CreatedAt,
                LastSeenAt = device.LastSeenAt,
                ApprovedByDeviceId = device.ApprovedByDeviceId
            };

        private static RemoteAccessPendingRequestViewModel ToViewModel(PendingPairingRequest request) =>
            new()
            {
                Id = request.Id,
                DeviceName = request.DeviceName,
                Platform = request.Platform,
                RemoteAddress = request.RemoteAddress,
                RequestedAt = request.RequestedAt,
                ExpiresAt = request.ExpiresAt
            };
    }
}
