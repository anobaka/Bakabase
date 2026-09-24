using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Prints a pairing code whenever a server with no screen is locked out of itself:
/// pairing is required, and no device has paired yet.
/// </summary>
/// <remarks>
/// <para>
/// In that state every remote caller is refused and there is nobody to approve a
/// request, so the only way in is a code — and on a server with no screen, the log is
/// the only place to put one. The container build is exactly that server.
/// </para>
/// <para>
/// The desktop app is not: its own window is on loopback, so it is never locked out of
/// itself, and it shows and issues codes on its settings and devices pages. A code in its
/// log would help nobody and is one more place for it to leak from: a log travels further
/// than a screen — it stays on disk, goes into bug reports, and is served to diagnostics
/// pages. So nothing is printed where there is a screen.
/// </para>
/// <para>
/// Whether there is one is read from the host's GUI adapter, the same test
/// <c>PlatformClientLauncher</c> makes: desktop hosts hand the Service a
/// <see cref="GuiAdapter"/>, the headless entry hands it <c>NullGuiAdapter</c> — which
/// holds for a development build too, whichever entry it was started from. Beyond that the
/// condition is the lockout itself rather than the runtime flavour. Gating on "is this the
/// container build" would read the same today and quietly exclude the headless server
/// build when it arrives; gating on the lockout keeps working, and says out loud what the
/// code is for.
/// </para>
/// <para>
/// Nothing is written, and no directory is created, unless the lockout actually holds:
/// the mode and the pairing switch are read first, and both come from options already
/// in memory.
/// </para>
/// </remarks>
/// <param name="gui">
/// The host's GUI adapter. Absent reads as no screen, which is what a host that registers
/// none has.
/// </param>
public sealed class FirstDevicePairingCodeAnnouncer(
    IRemoteAccessService remoteAccessService,
    IRemoteDeviceService deviceService,
    ILogger<FirstDevicePairingCodeAnnouncer> logger,
    IGuiAdapter? gui = null) : BackgroundService
{
    /// <summary>Whether this process has a screen of its own, which shows codes instead.</summary>
    public bool HasScreen { get; } = gui is GuiAdapter;

    /// <summary>
    /// How often the lockout is re-checked. Every input is already in memory, so this
    /// costs nothing; it is short enough that a code which lapses is replaced before
    /// anyone reading the log has time to type the old one.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (HasScreen)
        {
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            do
            {
                try
                {
                    await AnnounceIfLockedOutAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Never take the host down over this: the server still works for
                    // loopback, and a failure here only means nobody is told the code.
                    logger.LogError(e, "Failed to announce a pairing code");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>
    /// One pass of the check. Public so the rule about when a code is printed — which
    /// is the whole of this class — can be exercised without a running host.
    /// </summary>
    /// <returns>Whether a code was issued and printed.</returns>
    public async Task<bool> AnnounceIfLockedOutAsync(CancellationToken ct = default)
    {
        if (HasScreen || !IsLockedOut())
        {
            return false;
        }

        // A code the operator issued from the settings page counts; re-issuing would
        // invalidate the one they are in the middle of typing.
        if (deviceService.GetPairingCodeStatus() != null)
        {
            return false;
        }

        var issue = await deviceService.IssuePairingCodeAsync(ct: ct);
        Announce(issue.Code, issue.ExpiresAt);
        return true;
    }

    private bool IsLockedOut()
    {
        var mode = remoteAccessService.GetEffectiveMode();

        // Unrestricted ignores pairing altogether, so there is nothing to be locked out
        // of; Disabled serves nobody remote, and a code would not help.
        if (mode != RemoteAccessMode.Enabled || !remoteAccessService.GetRequirePairing())
        {
            return false;
        }

        return !deviceService.HasAnyDevice;
    }

    private void Announce(string code, DateTime expiresAt)
    {
        var minutes = Math.Max(1, (int) Math.Round((expiresAt - DateTime.UtcNow).TotalMinutes));
        var message =
            $"No device has paired with Bakabase yet, and pairing is required. " +
            $"Enter this code on the first device: {code} (valid for {minutes} minutes)";

        logger.LogWarning("{Message}", message);

        // Written to stdout as well as to the log, because a container's logging
        // configuration is not ours to assume and this is the one message an operator
        // cannot get any other way.
        Console.Out.WriteLine();
        Console.Out.WriteLine("  ================ Bakabase pairing code ================");
        Console.Out.WriteLine($"    {code}");
        Console.Out.WriteLine($"    valid for {minutes} minutes");
        Console.Out.WriteLine("  =======================================================");
        Console.Out.WriteLine();
        Console.Out.Flush();
    }
}
