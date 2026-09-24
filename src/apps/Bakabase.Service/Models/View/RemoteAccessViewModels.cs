using System.Collections.Generic;
using System;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

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

        /// <summary>Whether this caller's request carried a valid device signature.</summary>
        public bool Paired { get; set; }

        public string? DeviceId { get; set; }

        public string? DeviceName { get; set; }

        /// <summary>
        /// Which flavour is answering. A server answers for itself; a relay the desktop app
        /// runs for a server it manages answers this endpoint locally and says
        /// <see cref="Constants.ClientMode.PureClient"/> instead, which is the whole
        /// reason the field exists.
        /// </summary>
        public ClientMode ClientMode { get; set; }

        /// <summary>
        /// Always true here — a server that answered is reachable by definition. A relay's
        /// own answer can say otherwise, and the UI needs somewhere to read that from
        /// during a reconnect.
        /// </summary>
        public bool ServerReachable { get; set; } = true;

        /// <summary>Null on a server; a relay fills in the desktop app's version.</summary>
        public string? ClientVersion { get; set; }

        public string? ServerId { get; set; }

        public string? ServerName { get; set; }

        /// <summary>
        /// Whether a sign-in capture window can open for this caller. It needs a desktop
        /// and it needs to be this person's — neither holds for a browser on another
        /// device, nor for a container.
        /// </summary>
        public bool CookieCaptureAvailable { get; set; }
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

        /// <summary>
        /// Whether remote callers may start a live ffmpeg transcode. Off by default;
        /// remote playback of incompatible video is meant for native players.
        /// </summary>
        public bool AllowLiveTranscode { get; set; }

        /// <summary>Whether unpaired callers are refused outright.</summary>
        public bool RequirePairing { get; set; }

        public List<RemoteAccessDeviceViewModel> Devices { get; set; } = [];

        public List<RemoteAccessPendingRequestViewModel> PendingRequests { get; set; } = [];

        /// <summary>Null when no code is outstanding.</summary>
        public RemoteAccessPairingCodeViewModel? PairingCode { get; set; }
    }

    /// <summary>
    /// A paired device as the settings page shows it. Carries no key — the key exists
    /// on the server only to verify signatures and is never rendered anywhere.
    /// </summary>
    public record RemoteAccessDeviceViewModel
    {
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;

        public RemoteDevicePlatform Platform { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Null until the device makes its first signed request.</summary>
        public DateTime? LastSeenAt { get; set; }

        /// <summary>Which device let this one in; null for the first, which used a code.</summary>
        public string? ApprovedByDeviceId { get; set; }
    }

    public record RemoteAccessPendingRequestViewModel
    {
        public string Id { get; set; } = null!;

        public string DeviceName { get; set; } = null!;

        public RemoteDevicePlatform Platform { get; set; }

        /// <summary>Where it came from, so the approver can sanity-check it.</summary>
        public string? RemoteAddress { get; set; }

        public DateTime RequestedAt { get; set; }

        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Status of the outstanding code. The code itself appears only in the response that
    /// issued it.
    /// </summary>
    public record RemoteAccessPairingCodeViewModel
    {
        public DateTime ExpiresAt { get; set; }

        public int RemainingAttempts { get; set; }
    }

    /// <summary>The one response that carries a pairing code in plain text.</summary>
    public record RemoteAccessIssuedPairingCodeViewModel
    {
        public string Code { get; set; } = null!;

        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Credentials for a device that just paired. The key is returned here and never
    /// again — the device is expected to store it.
    /// </summary>
    public record RemoteAccessPairingCredentialsViewModel
    {
        public string DeviceId { get; set; } = null!;

        public string Key { get; set; } = null!;

        public string ServerId { get; set; } = null!;
    }

    /// <summary>
    /// The result of asking to pair, or of collecting the answer. Exactly one of
    /// <see cref="Credentials"/> and <see cref="Failure"/> is meaningful.
    /// </summary>
    public record RemoteAccessPairingResultViewModel
    {
        public RemoteAccessPairingCredentialsViewModel? Credentials { get; set; }

        public PairingFailure Failure { get; set; }
    }

    public record RemoteAccessPairingRequestAcceptedViewModel
    {
        /// <summary>
        /// Present this to collect credentials once somebody approves. It is the only
        /// thing identifying this request, so it is treated as a secret. Null when
        /// <see cref="Failure"/> says no request was filed.
        /// </summary>
        public string? RequestId { get; set; }

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Why no request was filed, if none was. Reported here rather than as a
        /// transport error so a client has one place to look across all three pairing
        /// endpoints.
        /// </summary>
        public PairingFailure Failure { get; set; }
    }

    /// <summary>
    /// What a client learns about this install — same facts as the discovery
    /// beacon broadcasts, so a manually-entered address ends in the same place
    /// as a discovered one.
    /// </summary>
    public record RemoteAccessServerInfoViewModel
    {
        /// <summary>Stable install identity; survives IP changes and restarts.</summary>
        public string Id { get; set; } = null!;

        /// <summary>Human-readable name to show in a server picker.</summary>
        public string Name { get; set; } = null!;

        public string AppVersion { get; set; } = null!;

        /// <summary>
        /// Remote API contract version; a client compares this against the range it
        /// supports before talking further.
        /// </summary>
        public int ProtocolVersion { get; set; }

        public RemoteAccessMode Mode { get; set; }

        /// <summary>
        /// Whether this server understands device pairing. A capability flag rather than
        /// a protocol bump: raising the protocol version would make every already
        /// installed client refuse to connect as "too new".
        /// </summary>
        public bool PairingSupported { get; set; }

        /// <summary>
        /// The server's clock, so a client can measure its offset and sign with a
        /// timestamp the server will accept.
        /// </summary>
        public DateTime ServerTime { get; set; }
    }
}
