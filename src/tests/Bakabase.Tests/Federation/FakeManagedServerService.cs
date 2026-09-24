using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;

namespace Bakabase.Tests.Federation;

/// <summary>
/// Stands in for the desktop app's relay manager: records what the controller asked for and
/// answers with fixed views, so a test sees exactly what crossed the HTTP boundary.
/// </summary>
internal sealed class FakeManagedServerService : IManagedServerService
{
    public const string KnownServerId = "server-1";

    public ConcurrentQueue<string> Calls { get; } = new();

    public IReadOnlyList<ManagedServerPathMapping>? LastMappings { get; private set; }

    public static readonly ManagedServerView Server = new(KnownServerId, "Living room NAS",
        "http://192.168.1.5:34567", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), null,
        [new ManagedServerPathMapping("/volume1/media", @"Z:\media")], ManagedServerState.Online,
        RemoteAccessMode.Unrestricted, "2.4.0", ImportedFromLegacyClient: true);

    public Task<ManagedServersView> GetAsync(bool probe, CancellationToken ct = default)
    {
        Calls.Enqueue($"Get probe={probe}");
        return Task.FromResult(new ManagedServersView(true, [Server],
        [
            // Still being collected, though the last claim did not get through.
            new ManagedServerPendingRequestView("request-1", "http://192.168.1.9:34567", "Study",
                new DateTime(2026, 1, 2, 3, 14, 5, DateTimeKind.Utc), ManagedServerOutcome.Unreachable, Active: true),
            // Over: kept in the listing for a while so the page can say how it ended.
            new ManagedServerPendingRequestView("request-3", "http://192.168.1.10:34567", "Attic",
                new DateTime(2026, 1, 2, 3, 14, 5, DateTimeKind.Utc), ManagedServerOutcome.RequestRejected,
                Active: false)
        ]));
    }

    public Task<ManagedServerProbeView> ProbeAsync(string address, CancellationToken ct = default)
    {
        Calls.Enqueue($"Probe {address}");
        return Task.FromResult(new ManagedServerProbeView(ManagedServerOutcome.Ok, "server-2", "Study", "2.4.0",
            RemoteAccessMode.Enabled, true, false, null));
    }

    public Task<ManagedServerDiscoveryView> DiscoverAsync(CancellationToken ct = default)
    {
        Calls.Enqueue("Discover");
        return Task.FromResult(new ManagedServerDiscoveryView(
        [
            new ManagedServerCandidateView(KnownServerId, "Living room NAS", "http://192.168.1.5:34567", "2.4.0",
                AlreadyManaged: true),
            new ManagedServerCandidateView("server-2", "Study", "http://192.168.1.9:34567", "2.5.0",
                AlreadyManaged: false)
        ]));
    }

    public Task<ManagedServerPairingView> PairAsync(string address, string? code, CancellationToken ct = default)
    {
        Calls.Enqueue($"Pair {address} code={code ?? "<none>"}");
        return Task.FromResult(code == null
            ? new ManagedServerPairingView(ManagedServerOutcome.AwaitingApproval, "server-2", "Study", "request-2",
                new DateTime(2026, 1, 2, 3, 14, 5, DateTimeKind.Utc), null)
            : new ManagedServerPairingView(ManagedServerOutcome.Ok, "server-2", "Study", null, null, null));
    }

    public Task<bool> CancelRequestAsync(string requestId, CancellationToken ct = default)
    {
        Calls.Enqueue($"Cancel {requestId}");
        return Task.FromResult(requestId == "request-1");
    }

    public Task<bool> ForgetAsync(string serverId, CancellationToken ct = default)
    {
        Calls.Enqueue($"Forget {serverId}");
        return Task.FromResult(serverId == KnownServerId);
    }

    public Task<bool> SetPathMappingsAsync(string serverId, IReadOnlyList<ManagedServerPathMapping> mappings,
        CancellationToken ct = default)
    {
        Calls.Enqueue($"Map {serverId} {mappings.Count}");
        LastMappings = mappings;
        return Task.FromResult(serverId == KnownServerId);
    }

    public Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default)
    {
        Calls.Enqueue($"Open {serverId} path={path ?? "<root>"}");
        return Task.FromResult(serverId == KnownServerId
            ? new ManagedServerOpenView($"http://127.0.0.1:34650{path ?? "/"}?__bakabase_switch=token")
            : null);
    }

    public Task<ManagedServerImportView> ImportFromLegacyClientAsync(CancellationToken ct = default)
    {
        Calls.Enqueue("Import");
        return Task.FromResult(new ManagedServerImportView(true, 2, 1));
    }
}
