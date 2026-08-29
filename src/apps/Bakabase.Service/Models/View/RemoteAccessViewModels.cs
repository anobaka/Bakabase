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
        /// connection, not guessed from the URL the browser happens to use — the host
        /// reaching itself by its LAN address is still local.
        /// </summary>
        public bool IsLocal { get; set; }

        public RemoteAccessMode Mode { get; set; }
    }

    public record RemoteAccessAddressViewModel
    {
        public string Url { get; set; } = null!;

        public string InterfaceName { get; set; } = null!;
    }

    public record RemoteAccessSettingsViewModel
    {
        public RemoteAccessMode Mode { get; set; }

        /// <summary>
        /// Addresses another device on the same network can open. Empty when no
        /// non-loopback interface is up.
        /// </summary>
        public List<RemoteAccessAddressViewModel> Addresses { get; set; } = [];
    }
}
