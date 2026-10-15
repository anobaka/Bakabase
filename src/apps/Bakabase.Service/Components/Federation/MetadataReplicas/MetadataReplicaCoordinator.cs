using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record ManagedMetadataStatus(ReplicaIdentity Identity, string? Name, ReplicaStatus Data,
    ReplicaSyncState Sync, bool Denied = false);

/// <summary>One bounded fetch at a time. Neither definitions nor foreign resource IDs enter the owned DB.</summary>
public sealed class MetadataReplicaCoordinator(IManagedServerService managed, MetadataReplicaStore store,
    MetadataReplicaRegistry registry)
{
    private readonly SemaphoreSlim _cycle = new(1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _ownerGates = new();

    public async Task<IAsyncDisposable> BeginWriteAsync(string serverId, string epoch, CancellationToken ct)
    {
        await RequireAsync(serverId, epoch, ct);
        var gate = _ownerGates.GetOrAdd(serverId, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var id = await RequireAsync(serverId, epoch, ct);
            // Persist before sending: a lost response or process exit must not expose the old image.
            await store.UpdateSyncStateAsync(id, state => state with {RefreshPending = true}, ct);
            return new OwnerLease(gate);
        }
        catch { gate.Release(); throw; }
    }

    public async Task EnsureFreshAsync(string serverId, string epoch, CancellationToken ct)
    {
        var id = await RequireAsync(serverId, epoch, ct);
        if (!(await store.GetSyncStateAsync(id, ct)).RefreshPending) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var gate = _ownerGates.GetOrAdd(serverId, _ => new SemaphoreSlim(1));
        var held = false;
        try
        {
            await gate.WaitAsync(deadline.Token);
            held = true;
            id = await RequireAsync(serverId, epoch, deadline.Token);
            if (!(await store.GetSyncStateAsync(id, deadline.Token)).RefreshPending) return;
            var connection = (await managed.GetAsync(false, deadline.Token)).Servers.Single(s => s.ServerId == serverId);
            await SyncOneAsync(connection, null, null, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        finally { if (held) gate.Release(); }
        id = await RequireAsync(serverId, epoch, ct);
        if ((await store.GetSyncStateAsync(id, ct)).RefreshPending)
            throw new FederationAccessException("MetadataRefreshPending", 503,
                "The latest resource data is still syncing. The current view is retained; refresh before submitting the change again.");
    }

    private sealed class OwnerLease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _gate, null)?.Release(); return ValueTask.CompletedTask; }
    }

    public async Task<IReadOnlyList<ManagedMetadataStatus>> StatusAsync(CancellationToken ct)
    {
        var connections = (await managed.GetAsync(false, ct)).Servers.Select(s => s.ServerId).ToHashSet();
        var result = new List<ManagedMetadataStatus>();
        foreach (var entry in await registry.ReadAsync(ct))
        {
            if (!connections.Contains(entry.ServerId)) continue;
            var id = new ReplicaIdentity(entry.ServerId, entry.LibraryEpoch);
            if (string.IsNullOrEmpty(entry.LibraryEpoch))
            {
                result.Add(new(id, entry.Name, new(null, null, 0, 0),
                    new(ErrorCode: entry.ErrorCode, Error: entry.Error, IsOffline: true), entry.Denied));
                continue;
            }
            result.Add(new(id, entry.Name, await store.GetStatusAsync(id, ct), await store.GetSyncStateAsync(id, ct), entry.Denied));
        }
        return result;
    }

    public async Task<ReplicaIdentity> RequireAsync(string serverId, string epoch, CancellationToken ct)
    {
        var connection = (await managed.GetAsync(false, ct)).Servers.SingleOrDefault(s => s.ServerId == serverId);
        if (connection == null || connection.State is ManagedServerState.Revoked or ManagedServerState.WrongServer)
            throw new FederationAccessException("ManagedSourceDenied", 403, "This library is no longer managed by this device.");
        var registration = (await registry.ReadAsync(ct)).SingleOrDefault(e => e.ServerId == serverId);
        if (registration?.Denied == true)
            throw new FederationAccessException("ManagedSourceDenied", 403, "Library access was revoked. Check the device pairing.");
        if (registration == null || string.IsNullOrEmpty(registration.LibraryEpoch))
            throw new FederationAccessException(registration?.ErrorCode ?? "MetadataSyncPending", 503,
                registration?.Error ?? "The library metadata is being prepared.");
        if (registration.LibraryEpoch != epoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "This library was replaced. Refresh its resources before continuing.");
        return new(serverId, epoch);
    }

    public async Task ControlAsync(string serverId, string epoch, bool paused, CancellationToken ct)
    {
        var id = await RequireAsync(serverId, epoch, ct);
        await store.UpdateSyncStateAsync(id, state => state with {Paused = paused, Error = null, ErrorCode = null}, ct);
    }

    public async Task RunCycleAsync(Func<string, long, Task>? progress, Func<Task>? yield,
        CancellationToken ct)
    {
        if (!await _cycle.WaitAsync(0, ct)) return;
        try
        {
            // Bounded peer enumeration, and no parallel network pages multiplying memory usage.
            foreach (var connection in (await managed.GetAsync(false, ct)).Servers.Take(1024))
            {
                if (yield != null) await yield();
                var gate = _ownerGates.GetOrAdd(connection.ServerId, _ => new SemaphoreSlim(1));
                if (!await gate.WaitAsync(0, ct)) continue;
                try { await SyncOneAsync(connection, progress, yield, ct); }
                finally { gate.Release(); }
            }
        }
        finally { _cycle.Release(); }
    }

    private async Task SyncOneAsync(ManagedServerView connection, Func<string, long, Task>? progress,
        Func<Task>? yield, CancellationToken ct)
    {
        var old = (await registry.ReadAsync(ct)).SingleOrDefault(r => r.ServerId == connection.ServerId);
        ReplicaIdentity? id = old == null || string.IsNullOrEmpty(old.LibraryEpoch) ? null : new(old.ServerId, old.LibraryEpoch);
        try
        {
            if (id != null && (await store.GetSyncStateAsync(id, ct)).Paused) return;
            var headResponse = await managed.SendAsync(connection.ServerId, HttpMethod.Get,
                "/management/library/metadata/head", null, ct);
            ThrowResponse(headResponse, 64 * 1024);
            var head = JsonConvert.DeserializeObject<MetadataSourceManifest>(headResponse.Body);
            if (head?.ContractVersion != 1 || head.Identity == null || head.Start == null || head.Identity.ServerId != connection.ServerId ||
                !NodeRequestSignature.IsIdentifier(head.Identity.LibraryEpoch) || string.IsNullOrWhiteSpace(head.Start.Generation) || head.Start.Sequence < 0 || head.Start.CatalogRevision < 0 || head.Start.Complete)
                throw new FederationAccessException("MetadataContractChanged", 409, "Update this device or verify its identity before synchronizing.");
            id = head.Identity;
            await registry.PutAsync(new(id.ServerId, id.LibraryEpoch, head.Name ?? connection.Name), ct);
            var data = await store.GetStatusAsync(id, ct);
            var checkpoint = data.Checkpoint;
            if (checkpoint == null || checkpoint.Generation != head.Start.Generation)
                checkpoint = await store.BeginSnapshotAsync(id, head.Start.Generation, head.Start.Sequence, head.Start.Cursor, ct);
            var timer = Stopwatch.StartNew();
            var pageSize = 50;
            for (var page = 0; page < 128 && timer.Elapsed < TimeSpan.FromSeconds(15); page++)
            {
                if (yield != null) await yield();
                ct.ThrowIfCancellationRequested();
                if ((await store.GetSyncStateAsync(id, ct)).Paused) break;
                var response = await managed.SendAsync(id.ServerId, HttpMethod.Post,
                    "/management/library/metadata/page?managedLibraryEpoch=" + Uri.EscapeDataString(id.LibraryEpoch),
                    JsonConvert.SerializeObject(new MetadataPageRequest(checkpoint, pageSize)), ct);
                if (response.StatusCode == 413 && pageSize > 1) { pageSize = Math.Max(1, pageSize / 2); continue; }
                ThrowResponse(response, MetadataReplicaStore.MaxBatchBytes);
                var batch = JsonConvert.DeserializeObject<ReplicaBatch>(response.Body)
                    ?? throw new InvalidDataException("The metadata page is empty.");
                if (batch.Expected == null || batch.Next == null || batch.Expected != checkpoint || batch.Next.Generation != checkpoint.Generation)
                    throw new InvalidDataException("The metadata page does not continue the committed checkpoint.");
                await store.ApplyBatchAsync(id, batch, ct);
                checkpoint = batch.Next;
                if (progress != null)
                {
                    var status = await store.GetStatusAsync(id, ct);
                    await progress(head.Name ?? connection.Name ?? id.ServerId,
                        checkpoint.Complete ? status.ResourceCount : status.StagedResourceCount);
                }
                if (checkpoint.Complete && checkpoint.Cursor == null && checkpoint.Sequence >= head.Start.Sequence &&
                    checkpoint.CatalogRevision >= head.Start.CatalogRevision) break;
            }
            await store.UpdateSyncStateAsync(id, state => state with {ErrorCode = null, Error = null, IsOffline = false,
                LastSyncedAtUtc = checkpoint.Complete && checkpoint.Cursor == null ? DateTimeOffset.UtcNow : state.LastSyncedAtUtc,
                RefreshPending = state.RefreshPending && !(checkpoint.Complete && checkpoint.Cursor == null && checkpoint.Sequence >= head.Start.Sequence &&
                    checkpoint.CatalogRevision >= head.Start.CatalogRevision)}, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is FederationAccessException or HttpRequestException or
            IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or JsonException or ArgumentException)
        {
            var denied = error is FederationAccessException {StatusCode: 401 or 403};
            if (id == null)
            {
                await registry.PutAsync(new(connection.ServerId, "", connection.Name, denied,
                    (error as FederationAccessException)?.ErrorCode ?? "MetadataSyncFailed", error.Message), ct);
                return; // Retain an explicit discovery error, never manufacture an empty library.
            }
            if (denied) await registry.PutAsync(new(id.ServerId, id.LibraryEpoch, old?.Name ?? connection.Name, Denied: true), ct);
            await store.UpdateSyncStateAsync(id, state => state with {ErrorCode = (error as FederationAccessException)?.ErrorCode ?? "MetadataSyncFailed",
                Error = error.Message, IsOffline = error is HttpRequestException or IOException or OperationCanceledException or
                    FederationAccessException {StatusCode: >= 500}}, ct);
        }
    }

    private static void ValidateResponseSize(ManagedServerResponse response, long limit)
    {
        if (response.Body == null || Encoding.UTF8.GetByteCount(response.Body) > limit)
            throw new ReplicaBudgetExceededException("The metadata response exceeds its byte budget.");
    }

    private static void ThrowResponse(ManagedServerResponse response, long maximumBytes)
    {
        if (response.StatusCode is >= 200 and < 300)
        {
            ValidateResponseSize(response, maximumBytes);
            // The ordinary host's legacy exception handler can return HTTP 200 with a
            // nonzero response code. Do not mistake that envelope for a broken cursor,
            // or expose its exception message/stack in another device's sync status.
            using var text = new StringReader(response.Body);
            using var reader = new JsonTextReader(text) {MaxDepth = 64};
            if (reader.Read() && reader.TokenType == JsonToken.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1) continue;
                    var isCode = string.Equals(reader.Value as string, "code", StringComparison.OrdinalIgnoreCase);
                    if (!reader.Read()) break;
                    if (isCode && !(reader.TokenType == JsonToken.Integer && Equals(reader.Value, 0L)) &&
                        !(reader.TokenType == JsonToken.String && Equals(reader.Value, "0")))
                        throw new FederationAccessException("MetadataSyncFailed", 502,
                            "The source did not complete the metadata request. The saved checkpoint is retained.");
                    // Stream over nested metadata without allocating another object tree.
                    reader.Skip();
                }
            }
            return;
        }
        throw new FederationAccessException(response.StatusCode switch
        {
            401 or 403 => "ManagedSourceDenied", 404 => "ManagementUpdateRequired", 409 => "MetadataGenerationChanged",
            413 => "MetadataPageTooLarge", _ => "MetadataSyncFailed"
        }, response.StatusCode, response.StatusCode switch
        {
            401 or 403 => "This device no longer allows library access.",
            404 => "Update this device to synchronize library metadata.",
            409 => "The library metadata changed. Synchronization will resume with its current version.",
            413 => "One resource exceeds the metadata size limit. Its data has not been skipped.",
            _ => "The source did not complete the metadata request. The saved checkpoint is retained."
        });
    }
}
