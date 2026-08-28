using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input
{
    public record RemoteDevicePairInputModel
    {
        [Required] public string PairingCode { get; set; } = null!;

        /// <summary>
        /// What the user calls this device, so it can be recognised (and revoked) in
        /// the device list later.
        /// </summary>
        public string? DeviceName { get; set; }

        public RemoteDevicePlatform Platform { get; set; }
    }

    public record RemoteAccessModeInputModel
    {
        /// <summary>
        /// Null resets to the runtime default.
        /// </summary>
        public RemoteAccessMode? Mode { get; set; }
    }

    public record RemoteDeviceRenameInputModel
    {
        [Required] public string Name { get; set; } = null!;
    }
}
