using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Service.Models.Input
{
    public record RemoteAccessModeInputModel
    {
        /// <summary>
        /// Null resets to the runtime default.
        /// </summary>
        public RemoteAccessMode? Mode { get; set; }
    }

    public record RemoteAccessAdvertisedAddressInputModel
    {
        public string? Address { get; set; }
    }

    public record RemoteAccessAddressCandidateInputModel
    {
        public string Address { get; set; } = "";
    }

    public record RemoteAccessLiveTranscodeInputModel
    {
        public bool Allow { get; set; }
    }

    public record RemoteAccessRequirePairingInputModel
    {
        public bool Require { get; set; }
    }

    /// <summary>
    /// The code is in the body rather than the URL so it stays out of access logs and
    /// proxy caches.
    /// </summary>
    public record RemoteAccessPairWithCodeInputModel
    {
        public string? Code { get; set; }

        /// <summary>Shown in the device list, so the operator can tell devices apart.</summary>
        public string? DeviceName { get; set; }

        public RemoteDevicePlatform Platform { get; set; }
    }

    public record RemoteAccessPairRequestInputModel
    {
        public string? DeviceName { get; set; }

        public RemoteDevicePlatform Platform { get; set; }
    }

    /// <summary>
    /// The request id is the only credential the waiting device has, so it travels in
    /// the body for the same reason the pairing code does.
    /// </summary>
    public record RemoteAccessPairClaimInputModel
    {
        public string? RequestId { get; set; }
    }

    public record RemoteAccessDeviceNameInputModel
    {
        public string? Name { get; set; }
    }
}
