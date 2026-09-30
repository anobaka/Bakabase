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
    /// <summary>A grant this device obtained (<paramref name="ReadBack"/>), or one it issued (<paramref name="Inbound"/>).</summary>
    private sealed record GrantEvent(string PeerNodeId, bool Inbound, string? ReadBack);

    private readonly ConcurrentQueue<GrantEvent> _queue = new();
    private readonly DataSyncLinkService _links;
    private readonly ILogger<DataSyncGrantEventsHandler> _logger;

    public DataSyncGrantEventsHandler(DataSyncLinkService links, ILogger<DataSyncGrantEventsHandler> logger)
    {
        _links = links;
        _logger = logger;
    }

    /// <summary>
    /// Our request or code was granted, and what the peer said of reading this device back (§7.2.4 step 7): a two-way
    /// link it declined to read back shows so, with "[Ask {name} to keep in step]".
    /// </summary>
    public void OutboundGranted(string peerNodeId, string? readBack = null) =>
        _queue.Enqueue(new GrantEvent(peerNodeId, false, readBack));

    public void InboundGranted(string peerNodeId) => _queue.Enqueue(new GrantEvent(peerNodeId, true, null));

    public bool HasPending => !_queue.IsEmpty;

    /// <summary>Handles every queued event in order. A failing event is logged and dropped.</summary>
    public async Task DrainAsync(CancellationToken ct)
    {
        while (_queue.TryDequeue(out var grantEvent))
        {
            try
            {
                if (grantEvent.Inbound)
                    await _links.OnInboundGrantedAsync(grantEvent.PeerNodeId, false, false, null, null, null, ct);
                else await _links.OnOutboundGrantedAsync(grantEvent.PeerNodeId, grantEvent.ReadBack, ct);
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
