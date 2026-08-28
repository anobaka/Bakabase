using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View
{
    /// <summary>
    /// What a client needs to know about its own standing, so the UI can decide
    /// between host-only actions and remote-friendly ones.
    /// </summary>
    public record RemoteAccessClientContextViewModel
    {
        /// <summary>
        /// True when the caller is on the machine running Bakabase. Derived from the
        /// connection, not guessed from the URL the browser happens to use.
        /// </summary>
        public bool IsLocal { get; set; }

        public RemoteAccessMode Mode { get; set; }

        public bool Paired { get; set; }

        public string? DeviceId { get; set; }

        public string? DeviceName { get; set; }
    }

    public record RemoteDevicePairingViewModel
    {
        /// <summary>
        /// Returned once, at pairing time; the server only keeps its hash. Also set as
        /// a cookie, so a browser client can ignore this field entirely.
        /// </summary>
        public string Token { get; set; } = null!;

        public string DeviceId { get; set; } = null!;

        public string DeviceName { get; set; } = null!;
    }

    public record RemoteAccessPairingCodeViewModel
    {
        public string Code { get; set; } = null!;

        public DateTime ExpiresAt { get; set; }
    }

    public record RemoteDeviceViewModel
    {
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;

        public RemoteDevicePlatform Platform { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastSeenAt { get; set; }
    }

    public record RemoteAccessSettingsViewModel
    {
        public RemoteAccessMode Mode { get; set; }

        /// <summary>
        /// The outstanding pairing code, if one is active. Null when none has been
        /// issued or it has expired.
        /// </summary>
        public string? PairingCode { get; set; }

        public DateTime? PairingCodeExpiresAt { get; set; }

        public List<RemoteDeviceViewModel> Devices { get; set; } = [];
    }
}
