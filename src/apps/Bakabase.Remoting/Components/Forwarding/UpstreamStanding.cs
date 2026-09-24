using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// How the server took the latest request this relay forwarded to it: the relay's own view
/// of whether the server it shows is there and still takes this device, kept current by the
/// window's own traffic at no cost.
/// </summary>
/// <remarks>
/// <para>
/// While a server's page is open in the window, that page is requesting through this relay
/// all the time — assets, API calls, the hub — so nothing knows better how the server is
/// doing right now. A probe from elsewhere is older by construction.
/// </para>
/// <para>
/// Only exchanges that actually went to the server count. The relay's own answers — its
/// <c>/client</c> API, the actions it runs on this machine, the guard's refusals — say
/// nothing about the server, and neither does the browser hanging up on a video it no
/// longer wants.
/// </para>
/// <para>
/// In memory only, one per relay, and read without waiting on anything: it is what a
/// listing can afford to consult.
/// </para>
/// </remarks>
public sealed class UpstreamStanding
{
    /// <summary>The header the server's gate names its reason for a refusal in.</summary>
    public const string DenialHeader = "X-Bakabase-Remote-Access";

    private volatile ManagedServerState _latest = ManagedServerState.Unknown;

    /// <summary>
    /// <see cref="ManagedServerState.Unknown"/> until something has been forwarded; then
    /// what the latest exchange said.
    /// </summary>
    public ManagedServerState Latest => _latest;

    /// <summary>The server answered <paramref name="response"/>, whatever it said.</summary>
    internal void Answered(HttpResponse response) =>
        _latest = Classify(response.StatusCode, response.Headers[DenialHeader].ToString());

    /// <summary>
    /// A forwarding error, as far as it says anything about the server: the ones the server
    /// or the way to it caused make it unreachable, the ones the browser caused are ignored.
    /// </summary>
    internal void Failed(ForwarderError error)
    {
        if (error is ForwarderError.Request or ForwarderError.RequestTimedOut
            or ForwarderError.RequestBodyDestination or ForwarderError.ResponseBodyDestination
            or ForwarderError.UpgradeRequestDestination or ForwarderError.UpgradeResponseDestination)
        {
            _latest = ManagedServerState.Offline;
        }
    }

    /// <summary>
    /// What an answer says about this device's standing on the server, read the way the
    /// console reads its own probe.
    /// </summary>
    /// <remarks>
    /// Anything the gate did not refuse is the server taking this device: every forwarded
    /// request is signed, and the gate checks the signature before any route runs, so even
    /// a 404 or a 500 means the key was accepted. A refusal is about this device only when
    /// it says so; one about a path (host-only, outside the libraries, a transcode not
    /// allowed) is the server answering normally.
    /// </remarks>
    public static ManagedServerState Classify(int status, string? denial)
    {
        if (status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return ManagedServerState.Online;
        }

        return denial switch
        {
            nameof(RemoteAccessDenialReason.DeviceRevoked) or nameof(RemoteAccessDenialReason.Unauthenticated) =>
                ManagedServerState.Revoked,
            // Switched off, or a clock too far out to sign for: not this device's standing,
            // just not usable right now.
            nameof(RemoteAccessDenialReason.Disabled) or nameof(RemoteAccessDenialReason.SignatureExpired) =>
                ManagedServerState.Offline,
            _ => ManagedServerState.Online
        };
    }
}
