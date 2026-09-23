using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Headless setup for a NAS or container, where nobody can open the loopback-only Devices page:
/// <c>BAKABASE_FEDERATION_SHARING=true</c> turns read-only sharing on at startup, and
/// <c>--federation-invite-on-start</c> prints a one-time code for the next device to pair.
/// </summary>
public sealed class FederationInviteAnnouncer(FederationStateStore store, FederationPeerService peers,
    IRemoteAccessService remoteAccess, ILogger<FederationInviteAnnouncer> logger) : IHostedService
{
    public const string SharingVariable = "BAKABASE_FEDERATION_SHARING";

    public async Task StartAsync(CancellationToken ct)
    {
        var enableSharing = bool.TryParse(Environment.GetEnvironmentVariable(SharingVariable), out var sharing) && sharing;
        var announce = Environment.GetCommandLineArgs().Contains("--federation-invite-on-start");
        if (!enableSharing && !announce) return;
        try
        {
            if (enableSharing && !await store.IsSharingEnabledAsync(ct))
            {
                // Only sharing: the remote-access mode stays whatever the operator configured.
                await peers.SetSharingAsync(true, ct);
                Console.WriteLine($"Bakabase resource sharing enabled by {SharingVariable}.");
            }
            if (!announce) return;
            if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled || !await store.IsSharingEnabledAsync(ct))
            {
                Console.WriteLine("Federation invitation unavailable: enable resource sharing and remote access first.");
                return;
            }
            var invite = await peers.IssueInvitationAsync(ct);
            Console.WriteLine($"Bakabase resource sharing invitation: {invite.Code} (expires {invite.ExpiresAt:O}). " +
                              "This grants read access to this node's local library only.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A damaged sharing state must not stop the whole service from starting.
            logger.LogError(e, "Federation invitation could not be issued");
            Console.WriteLine($"Federation invitation unavailable: {e.Message}");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
