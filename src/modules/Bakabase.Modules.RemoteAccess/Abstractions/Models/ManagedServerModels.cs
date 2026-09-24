using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// What happened when this device tried to reach, or get management rights on, another
/// server. One vocabulary for inspecting an address, pairing with a code, filing a request
/// and collecting its answer, because the UI shows all four in the same place.
/// </summary>
public enum ManagedServerOutcome
{
    Ok = 0,

    /// <summary>Filed and waiting for somebody on the other device to approve it. Not an error.</summary>
    AwaitingApproval = 1,

    /// <summary>Nothing answered. Wrong address, server not running, or the network is down.</summary>
    Unreachable = 2,

    /// <summary>Something answered, but not a Bakabase server.</summary>
    NotBakabase = 3,

    /// <summary>The server speaks a newer remote contract than this app. Update this app.</summary>
    ThisAppTooOld = 4,

    /// <summary>The server is older than anything this app can manage. Update the server.</summary>
    ServerTooOld = 5,

    /// <summary>Remote access is switched off on the server, so nothing can manage it.</summary>
    RemoteAccessDisabled = 6,

    /// <summary>The address is this device's own, or a relay this device runs.</summary>
    ThisDevice = 7,

    /// <summary>Wrong code, expired, or burned by earlier wrong guesses.</summary>
    CodeRejected = 8,

    /// <summary>The request was rejected, or waited too long and lapsed.</summary>
    RequestRejected = 9,

    /// <summary>The server refused to take any more requests from here for now.</summary>
    TooManyAttempts = 10,

    /// <summary>The server cannot pair at all (too old, or pairing disabled there).</summary>
    PairingUnsupported = 11
}

/// <summary>How a managed server looked the last time this device asked.</summary>
public enum ManagedServerState
{
    /// <summary>Not asked yet (a listing without probing).</summary>
    Unknown = 0,

    /// <summary>Answered and still accepts this device's key.</summary>
    Online = 1,

    /// <summary>Did not answer.</summary>
    Offline = 2,

    /// <summary>Answered but no longer knows this device: revoked there.</summary>
    Revoked = 3,

    /// <summary>
    /// Its address answers as another server — or as this device itself — so nothing is sent
    /// there. The server moved (a new port after a restart, a new DHCP lease), or it was
    /// reinstalled or its data reset and is a new install now. See
    /// <see cref="ManagedServerView.AnsweredBy"/> for who answers instead.
    /// </summary>
    WrongServer = 4
}

/// <summary>Who answered at a managed server's address instead of that server.</summary>
/// <param name="ServerId">Its install identity, when it gave one.</param>
/// <param name="Name">What it calls itself, when it said.</param>
/// <param name="IsThisDevice">The address now reaches this device itself: its own server, or one of its relays.</param>
public sealed record ManagedServerAnswerView(string? ServerId, string? Name, bool IsThisDevice);

/// <summary>Where one of a managed server's library paths is on this machine.</summary>
public sealed record ManagedServerPathMapping(string ServerPath, string LocalPath);

/// <summary>
/// A server this device can switch its window to and manage in full.
/// </summary>
/// <remarks>
/// Never carries the key: it stays in the managed-server store, which only the relay
/// reads. Deliberately separate from a federation peer — that is a read-only grant
/// between two libraries; this is the other server's own administrator access.
/// </remarks>
/// <param name="ServerId">The server's stable install identity.</param>
/// <param name="Name">What it calls itself; refreshed whenever it answers.</param>
/// <param name="Address">Base address, e.g. <c>http://192.168.1.5:34567</c>.</param>
/// <param name="Mode">
/// The server's remote-access mode when last probed. <see cref="RemoteAccessMode.Unrestricted"/>
/// means anybody on its network can manage it without pairing; the UI warns and changes nothing.
/// </param>
/// <param name="AppVersion">The server's version when last probed.</param>
/// <param name="ImportedFromLegacyClient">Brought over from the removed thin client rather than paired here.</param>
/// <param name="AnsweredBy">
/// Set only while <paramref name="State"/> is <see cref="ManagedServerState.WrongServer"/>:
/// who answers at <paramref name="Address"/> instead. Never used as this server's name, mode
/// or version, which stay as they were last seen.
/// </param>
/// <param name="Kind">
/// What kind of install it said it is when last probed, if it said — like <paramref name="Mode"/>
/// and <paramref name="AppVersion"/>, never taken from whoever answers in its place.
/// </param>
/// <param name="Platform">What it said it runs on when last probed, if it said.</param>
public sealed record ManagedServerView(
    string ServerId,
    string? Name,
    string Address,
    DateTime PairedAt,
    DateTime? LastConnectedAt,
    IReadOnlyList<ManagedServerPathMapping> PathMappings,
    ManagedServerState State,
    RemoteAccessMode? Mode,
    string? AppVersion,
    bool ImportedFromLegacyClient,
    ManagedServerAnswerView? AnsweredBy = null,
    ServerKind? Kind = null,
    RemoteDevicePlatform? Platform = null);

/// <summary>A management request this device filed, and where waiting on it has got to.</summary>
/// <param name="Outcome">
/// What the last attempt to collect it said. While <paramref name="Active"/> this can be a
/// passing failure — <see cref="ManagedServerOutcome.Unreachable"/> for one claim the network
/// dropped, <see cref="ManagedServerOutcome.TooManyAttempts"/> for one the server throttled —
/// that did not end the wait; once not active it is how the request ended.
/// </param>
/// <param name="Active">
/// True while this device is still waiting on the request and claiming its answer, whatever
/// the last attempt said. False once it has ended: approved (the server then joins the
/// managed list and the request leaves this one), rejected, expired, or cancelled. The one
/// signal to poll on and to offer "cancel" for; <paramref name="Outcome"/> alone cannot say
/// whether the wait is over.
/// </param>
/// <param name="ServerId">
/// The install the request was filed with, as its address answered the pairing handshake —
/// the identity the server joins the managed list under once approved.
/// </param>
public sealed record ManagedServerPendingRequestView(
    string RequestId,
    string Address,
    string? ServerName,
    DateTime ExpiresAt,
    ManagedServerOutcome Outcome,
    bool Active,
    string? ServerId = null);

/// <summary>What an address turned out to be, before anything was paired.</summary>
public sealed record ManagedServerProbeView(
    ManagedServerOutcome Outcome,
    string? ServerId,
    string? Name,
    string? AppVersion,
    RemoteAccessMode? Mode,
    bool PairingSupported,
    bool AlreadyManaged,
    string? Detail);

/// <summary>The result of pairing with a code or filing a request.</summary>
/// <param name="RequestId">Set while a filed request waits for approval; the app collects the answer itself.</param>
public sealed record ManagedServerPairingView(
    ManagedServerOutcome Outcome,
    string? ServerId,
    string? ServerName,
    string? RequestId,
    DateTime? ExpiresAt,
    string? Detail);

/// <summary>Where the window should go to show a server.</summary>
/// <param name="Url">A loopback relay URL carrying a single-use navigation token, or this device's own origin.</param>
public sealed record ManagedServerOpenView(string Url);

/// <summary>What importing the removed thin client's pairings did.</summary>
/// <param name="Found">Whether a thin-client installation with pairings exists on this machine.</param>
/// <param name="Imported">Servers added; ones already managed here are left as they are.</param>
public sealed record ManagedServerImportView(bool Found, int Imported, int Skipped);

/// <summary>A server on this network that this device could manage.</summary>
/// <remarks>
/// Found by the remote-access beacons — the UDP probe and mDNS every server answers while
/// remote access is on — not by library sharing, which is off by default and says nothing
/// about whether a server can be managed. Only a hint: pairing still asks the address who
/// it is.
/// </remarks>
/// <param name="ServerId">The server's stable install identity, as its beacon reports it.</param>
/// <param name="Name">What it calls itself.</param>
/// <param name="Address">The base address that answered, e.g. <c>http://192.168.1.5:34567</c>.</param>
/// <param name="AppVersion">Its version, as its beacon reports it.</param>
/// <param name="AlreadyManaged">This device already manages it.</param>
/// <param name="Kind">What kind of install its beacon says it is, when it says.</param>
/// <param name="Platform">What its beacon says it runs on, when it says.</param>
public sealed record ManagedServerCandidateView(
    string ServerId,
    string Name,
    string Address,
    string AppVersion,
    bool AlreadyManaged,
    ServerKind? Kind = null,
    RemoteDevicePlatform? Platform = null);

/// <summary>What looking around the network for servers to manage found. Never this device itself.</summary>
public sealed record ManagedServerDiscoveryView(IReadOnlyList<ManagedServerCandidateView> Servers);

/// <summary>Everything the devices page needs about management in one call.</summary>
/// <param name="Available">False where nothing can be managed from here (a headless server).</param>
public sealed record ManagedServersView(
    bool Available,
    IReadOnlyList<ManagedServerView> Servers,
    IReadOnlyList<ManagedServerPendingRequestView> Requests);
