using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.Federation;

/// <summary>An explicit one-shot console operation for a headless owner; it does not enable sharing.</summary>
public sealed class FederationInviteAnnouncer(FederationStateStore store, FederationPeerService peers,
    IRemoteAccessService remoteAccess) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!Environment.GetCommandLineArgs().Contains("--federation-invite-on-start")) return;
        if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled || !await store.IsSharingEnabledAsync(ct))
        {
            Console.WriteLine("Federation invitation unavailable: enable resource sharing and remote access first.");
            return;
        }
        var invite = await peers.IssueInvitationAsync(ct);
        Console.WriteLine($"Bakabase resource sharing invitation: {invite.Code} (expires {invite.ExpiresAt:O}). " +
                          "This grants read access to this node's local library only.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
