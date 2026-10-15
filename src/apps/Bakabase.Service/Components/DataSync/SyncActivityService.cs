using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Components.Federation.Processing;

namespace Bakabase.Service.Components.DataSync;

public sealed record SyncActivityDevice(string Id, string Name);
/// <summary>Presentation of existing engines, not a new synchronization protocol. Source and
/// destination describe stored data; a worker computes it but never owns it.</summary>
public sealed record SyncActivityRow(string Id, string Engine, string DataType, string State,
    string Phase, SyncActivityDevice Source, SyncActivityDevice Destination,
    DateTimeOffset? LastSuccessAtUtc = null, long? ItemCount = null, string? ErrorCode = null,
    string? Error = null, bool CachedDataAvailable = false, int? LinkId = null,
    string? LibraryEpoch = null, SyncActivityDevice? Worker = null, ResourceProcessingJobView? Job = null,
    int PendingDecisions = 0);
public sealed record SyncActivityPage(IReadOnlyList<SyncActivityRow> Items, int Total, int? NextOffset,
    IReadOnlyList<SyncActivityDevice> Devices, IReadOnlyList<string> DataTypes);

/// <summary>Read local durable state only: visiting the page never probes a device or fetches resource payloads.</summary>
public sealed class SyncActivityService(IDataSyncService definitions, MetadataReplicaCoordinator metadata,
    MetadataReplicaStore replicas, ResourceProcessingJobStore processing, BakabaseDbContext db)
{
    public async Task<SyncActivityPage> GetAsync(string? dataType, string? deviceId, string? state,
        int offset, int take, CancellationToken ct)
    {
        var overview = await definitions.GetOverviewAsync(ct);
        var self = new SyncActivityDevice(overview.NodeId, overview.DeviceName);
        var rows = new List<SyncActivityRow>();
        var names = new Dictionary<string, SyncActivityDevice> { [self.Id] = self };
        var decisions = (await db.DataSyncInboxItems.AsNoTracking().Where(item => item.ClosedAtUtc == null)
            .GroupBy(item => new {item.LinkId, item.Kind})
            .Select(group => new {group.Key.LinkId, group.Key.Kind, Count = group.Count()}).ToArrayAsync(ct))
            .ToDictionary(item => (item.LinkId, item.Kind), item => item.Count);
        var pending = (await db.DataSyncPeerBases.AsNoTracking().Where(item => item.PendingRecordJson != null)
            .GroupBy(item => new {item.LinkId, item.Kind})
            .Select(group => new {group.Key.LinkId, group.Key.Kind, Count = group.Count()}).ToArrayAsync(ct))
            .ToDictionary(item => (item.LinkId, item.Kind), item => item.Count);
        foreach (var link in await definitions.GetLinksAsync(ct))
        {
            var source = new SyncActivityDevice(link.PeerNodeId, link.PeerName);
            names[source.Id] = source;
            foreach (var kind in link.Kinds)
                rows.Add(new($"definitions:{link.Id}:{kind}", "definitions", "definitions." + kind,
                    DefinitionState(link with {OpenItems = decisions.GetValueOrDefault(((int?)link.Id, kind)), PendingCount = pending.GetValueOrDefault((link.Id, kind))}, overview.AllPaused), link.FullReconciliationRunning ? "Comparing" : link.State.ToString(),
                    source, self, Utc(link.LastSyncedAt), ErrorCode: link.LastErrorCode ?? link.PausedReason?.ToString(),
                    Error: link.LastErrorDetail ?? link.PausedDetail, LinkId: link.Id, PendingDecisions: decisions.GetValueOrDefault(((int?)link.Id, kind))));
        }
        // A reader's last report is a different direction from our own committed apply.
        // It cannot be inferred from this device's BTask or claimed current merely because access exists.
        foreach (var reader in await definitions.GetReadersAsync(ct))
        {
            var destination = new SyncActivityDevice(reader.NodeId, reader.Name);
            names[destination.Id] = destination;
            foreach (var kind in overview.Kinds)
                rows.Add(new($"reader:{reader.NodeId}:{kind.Kind}", "definitions", "definitions." + kind.Kind,
                    ReaderState(reader), "PeerReport", self, destination,
                    Utc(reader.LastReadAt), Error: null));
        }
        foreach (var source in await metadata.StatusAsync(ct))
        {
            var device = new SyncActivityDevice(source.Identity.ServerId, source.Name ?? source.Identity.ServerId);
            names[device.Id] = device;
            var counts = string.IsNullOrEmpty(source.Identity.LibraryEpoch)
                ? new Dictionary<string, long>() : await replicas.GetCatalogCountsAsync(source.Identity, ct);
            var metadataState = MetadataState(source);
            foreach (var kind in new[] { "resources", "properties", "customProperties", "mediaLibraries", "collections", "extensionGroups" })
            {
                var count = kind == "resources" ? source.Data.ActiveGeneration != null ? source.Data.ResourceCount : source.Data.StagedResourceCount : counts.GetValueOrDefault(kind);
                rows.Add(new($"metadata:{device.Id}:{kind}", "metadata", "metadata." + kind, metadataState,
                    source.ActivePhase ?? (source.Data.ActiveGeneration == null ? "Snapshot" : source.Sync.RefreshPending ? "Refreshing" : "Changes"),
                    device, self, source.Sync.LastSyncedAtUtc, count, source.Sync.ErrorCode, source.Sync.Error,
                    source.Data.ActiveGeneration != null && !source.Denied, LibraryEpoch: source.Identity.LibraryEpoch));
            }
        }
        // A compact recent-task overview; all retained tasks remain pageable through processing history.
        foreach (var role in Enum.GetValues<ResourceProcessingRole>())
        {
            var recent = await processing.ListAsync(role, null, 30, ct);
            var active = await processing.ListAsync(role, null, 100, ct, activeOnly: true);
            foreach (var job in active.Concat(recent).DistinctBy(j => j.Id))
            {
                var owner = names.GetValueOrDefault(job.OwnerId) ?? new SyncActivityDevice(job.OwnerId, job.OwnerId);
                var workerId = await processing.WorkerIdAsync(job.Id, self.Id, ct);
                var worker = workerId == null ? null : names.GetValueOrDefault(workerId) ?? new SyncActivityDevice(workerId, workerId);
                rows.Add(new($"processing:{job.Id}", "processing", "processing." + job.Kind,
                    job.State, job.Phase ?? job.State, owner, owner,
                    job.State == "Completed" ? Utc(job.UpdatedAtUtc) : null, job.ProcessedCount, job.ErrorCode, job.Error,
                    Worker: worker, Job: job));
                names.TryAdd(owner.Id, owner);
                if (worker != null) names.TryAdd(worker.Id, worker);
            }
        }
        var kinds = rows.Select(r => r.DataType).Distinct().Order().ToArray();
        var filtered = rows.Where(row => (dataType == null || row.DataType == dataType) &&
            (deviceId == null || row.Source.Id == deviceId || row.Destination.Id == deviceId || row.Worker?.Id == deviceId) &&
            (state == null || row.State == state)).OrderBy(row => Priority(row.State)).ThenBy(row => row.DataType)
            .ThenBy(row => row.Source.Name).ThenBy(row => row.Id).ToArray();
        offset = Math.Max(0, offset); take = Math.Clamp(take, 1, 100);
        return new(filtered.Skip(offset).Take(take).ToArray(), filtered.Length,
            offset + take < filtered.Length ? offset + take : null, names.Values.OrderBy(d => d.Name).ToArray(), kinds);
    }

    public static string DefinitionState(DataSyncLinkView link, bool allPaused) =>
        link.PausedReason is DataSyncPauseReason.PeerReset or DataSyncPauseReason.PeerIdentityDuplicated or
            DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected ? "IdentityChanged" :
        link.LastErrorCode is "PeerTooOld" or "ThisTooOld" ? "UpdateRequired" :
        link.LastErrorCode is "IdentityConflict" or "PeerReset" ? "IdentityChanged" :
        link.LastErrorCode is "AccessRevoked" or "AccessMissing" or "PeerSharingOff" or "PeerRemoteAccessOff" ? "Denied" :
        allPaused || link.State == DataSyncLinkState.Paused ? "Paused" :
        link.OpenItems > 0 || link.State == DataSyncLinkState.AwaitingReview ? "NeedsReview" :
        link.LastErrorCode == "Unreachable" ? "Offline" :
        link.LastErrorCode == "Busy" ? "Syncing" :
        link.LastErrorCode != null ? "Failed" :
        link.State == DataSyncLinkState.Stopped ? "Stopped" :
        link.State != DataSyncLinkState.Active ? "Waiting" :
        link.FullReconciliationRunning || link.PendingCount > 0 ? "Syncing" :
        link.LastSyncedAt == null ? "Waiting" : "Current";

    public static string ReaderState(DataSyncReaderView reader) =>
        reader.State?.StartsWith("paused", StringComparison.Ordinal) == true ? "Paused" :
        reader.State?.StartsWith("needsYou", StringComparison.Ordinal) == true || reader.State == "awaitingReview" ? "NeedsReview" :
        reader.State == "waitingForPeerReview" ? "Waiting" :
        reader.UpToDate ? "ReportedCurrent" : "Waiting";

    public static string MetadataState(ManagedMetadataStatus source) => source.Denied ? "Denied" :
        source.Sync.ErrorCode is "ManagementUpdateRequired" or "MetadataContractChanged" ? "UpdateRequired" :
        source.Sync.ErrorCode is "LibraryEpochChanged" or "WrongServer" or "SameIdentity" ? "IdentityChanged" :
        source.Sync.Paused ? "Paused" : source.Sync.IsOffline ? "Offline" : source.Sync.ErrorCode != null ? "Failed" :
        source.ActivePhase != null ? "Syncing" : source.Sync.RefreshPending || source.Sync.CatchupPending || source.Data.Checkpoint is not {Complete: true, Cursor: null} ? "Syncing" : "Current";

    private static int Priority(string state) => state switch {
        "Failed" or "Conflict" or "IdentityChanged" or "UpdateRequired" or "NeedsReview" or "Denied" => 0,
        "Running" or "Syncing" or "Queued" => 1, "Offline" or "Paused" or "Waiting" => 2, _ => 3 };
    private static DateTimeOffset? Utc(DateTime? value) => value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
