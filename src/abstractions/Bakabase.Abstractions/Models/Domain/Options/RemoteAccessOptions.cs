using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Models.Domain.Options;

/// <summary>
/// Controls whether devices other than the host machine may use Bakabase.
/// <para>
/// Nothing here affects loopback requests, so the desktop app is unaffected by
/// every setting on this class.
/// </para>
/// <para>
/// Caveat: "loopback" is decided from the connection's peer address. Putting a
/// reverse proxy in front of Bakabase on the same machine makes every request
/// look local and bypasses the gate — that is also what happens today, without
/// any gate at all, but it means a proxied deployment has to do its own
/// authentication. Bakabase deliberately does not trust forwarded headers, since
/// a client could otherwise claim to be local just by sending one.
/// </para>
/// </summary>
[Options(fileKey: "remote-access")]
public class RemoteAccessOptions
{
    /// <summary>
    /// Explicit mode chosen by the user. Null means "use the runtime default",
    /// which is <see cref="RemoteAccessMode.Open"/> under Docker (preserving the
    /// behavior containerized installs have always had) and
    /// <see cref="RemoteAccessMode.Disabled"/> everywhere else.
    /// </summary>
    public RemoteAccessMode? Mode { get; set; }

    /// <summary>
    /// HMAC key for signed media URLs. Generated on demand; never leaves the server.
    /// </summary>
    public string? SigningSecret { get; set; }

    /// <summary>
    /// Short code a new device presents once to pair. Cleared after it expires or
    /// is consumed.
    /// </summary>
    public string? PairingCode { get; set; }

    public DateTime? PairingCodeExpiresAt { get; set; }

    public List<RemoteDevice> Devices { get; set; } = [];

    /// <summary>
    /// Whether remote clients may trigger the live ffmpeg transcode path. Off by
    /// default: remote playback of an incompatible video is meant to be handed to a
    /// native player rather than burning host CPU per viewer.
    /// </summary>
    public bool AllowLiveTranscode { get; set; }
}
