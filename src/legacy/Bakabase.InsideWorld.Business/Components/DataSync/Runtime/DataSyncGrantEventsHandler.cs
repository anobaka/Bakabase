using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Grant events from the pairing flow (§7.2, §8.2), raised synchronously by D and handled by the scheduler within a
/// second: they are queued here and drained on the scheduler's next tick, so the caller never waits for the
/// database and a failure never reaches the pairing flow.
/// </summary>
public sealed class DataSyncGrantEventsHandler : IDataSyncGrantEvents
{
    private abstract record GrantEvent(string PeerNodeId);

    private sealed record Outbound(string PeerNodeId) : GrantEvent(PeerNodeId);

    private sealed record Inbound(string PeerNodeId, DataSyncRequestIntent Intent, bool ReadBackStarted)
        : GrantEvent(PeerNodeId);

    private sealed record ReadBack(string PeerNodeId, string ErrorCode) : GrantEvent(PeerNodeId);

    private readonly ConcurrentQueue<GrantEvent> _queue = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly DataSyncLinkService _links;
    private readonly ILogger<DataSyncGrantEventsHandler> _logger;

    public DataSyncGrantEventsHandler(IServiceScopeFactory scopes, DataSyncLinkService links,
        ILogger<DataSyncGrantEventsHandler> logger)
    {
        _scopes = scopes;
        _links = links;
        _logger = logger;
    }

    public void OutboundGranted(string peerNodeId) => _queue.Enqueue(new Outbound(peerNodeId));

    public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted) =>
        _queue.Enqueue(new Inbound(peerNodeId, intent, readBackStarted));

    /// <summary>
    /// A read-back announced by <see cref="InboundGranted"/> failed (§7.2.4, N14): the approver's link records why it
    /// still waits for access. Raised after the approval too when the read-back runs in the background (a code redeemed
    /// two-way), where nothing else would tell the link.
    /// </summary>
    public void ReadBackFailed(string peerNodeId, string errorCode) =>
        _queue.Enqueue(new ReadBack(peerNodeId, errorCode));

    public bool HasPending => !_queue.IsEmpty;

    /// <summary>Handles every queued event in order. A failing event is logged and dropped.</summary>
    public async Task DrainAsync(CancellationToken ct)
    {
        while (_queue.TryDequeue(out var grantEvent))
        {
            try
            {
                switch (grantEvent)
                {
                    case Outbound outbound:
                        await _links.OnOutboundGrantedAsync(outbound.PeerNodeId, ct);
                        break;
                    case Inbound inbound:
                    {
                        // The event says whether a read-back was started, not whether it gave this device access:
                        // the grant itself says that (§7.2.4).
                        var readBackGranted = false;
                        if (inbound.ReadBackStarted)
                        {
                            await using var scope = _scopes.CreateAsyncScope();
                            readBackGranted = await scope.ServiceProvider.GetRequiredService<IDataSyncGrantService>()
                                .HasOutboundGrantAsync(inbound.PeerNodeId, ct);
                        }

                        await _links.OnInboundGrantedAsync(inbound.PeerNodeId, inbound.Intent, inbound.ReadBackStarted,
                            readBackGranted, null, null, null, ct);
                        break;
                    }
                    case ReadBack readBack:
                        await _links.OnReadBackFailedAsync(readBack.PeerNodeId, readBack.ErrorCode, ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _queue.Enqueue(grantEvent);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Data sync could not handle a grant event for {Peer}", grantEvent.PeerNodeId);
            }
        }
    }
}
