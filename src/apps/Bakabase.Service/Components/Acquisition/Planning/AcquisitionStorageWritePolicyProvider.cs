using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>The host supplies live acquisition policy; generic writers never reference acquisition models.</summary>
public sealed class AcquisitionStorageWritePolicyProvider(IServiceScopeFactory scopes, IBOptions<AcquisitionOptions> options)
    : IStorageWritePolicyProvider
{
    private static long _grantEpoch;
    private readonly ConcurrentDictionary<(string Kind, string Id), (long Epoch, DateTime Until)> _normalOwners = new();
    internal static void Invalidate() => Interlocked.Increment(ref _grantEpoch);

    public async Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default)
    {
        if (context == null) return 0;
        var requested = context.MinimumFreeBytes ?? 0;
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(context));
        var key = (context.OwnerKind, context.OwnerId);
        var epoch = Volatile.Read(ref _grantEpoch);
        // Cache only the conservative global policy. Never cache lowered reserves or absence
        // of acquisition owners: a newly shared operation must not inherit stale permission.
        if (!options.Value.UseEmergencySpaceReserve && _normalOwners.TryGetValue(key, out var cached) && cached.Epoch == epoch && cached.Until > DateTime.UtcNow)
            return Math.Max(requested, options.Value.MinimumFreeSpaceBytes);
        var settings = options.Value;
        var (owned, reserve) = await ResolveAsync(context, settings, ct);
        if (owned && !settings.UseEmergencySpaceReserve && reserve == settings.MinimumFreeSpaceBytes) _normalOwners[key] = (epoch, DateTime.UtcNow.AddSeconds(5));
        return Math.Max(requested, reserve);
    }

    private async Task<(bool Owned, long Minimum)> ResolveAsync(StorageWriteContext context, AcquisitionOptions settings, CancellationToken ct)
    {
        if (context.OwnerKind is not ("workflow" or "downloadTask" or "cloudOperation" or "artifactOperation")) return (false, 0);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var runs = new HashSet<int>();
        var taskIds = new HashSet<int>();
        var unknown = false;
        var owned = false;
        void Add(string? value)
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) runs.Add(id);
            else unknown = true;
        }
        if (context.OwnerKind == "workflow") Add(context.OwnerId);
        else if (context.OwnerKind == "downloadTask" && int.TryParse(context.OwnerId, out var downloadId))
        {
            foreach (var owner in await db.Set<DownloadResultOwnerDbModel>().AsNoTracking()
                         .Where(o => o.DownloadTaskId == downloadId).Select(o => new {o.AcquisitionTaskId, o.WorkflowRunId}).ToListAsync(ct))
            {
                owned = true; taskIds.Add(owner.AcquisitionTaskId); runs.Add(owner.WorkflowRunId);
            }
            foreach (var id in await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == downloadId)
                         .Select(o => o.WorkflowRunId).ToListAsync(ct)) runs.Add(id);
        }
        else if (context.OwnerKind is "cloudOperation" or "artifactOperation")
        {
            if (context.OwnerKind == "cloudOperation")
                foreach (var id in await db.CloudDownloadOperations.AsNoTracking().Where(o => o.Id == context.OwnerId)
                             .Select(o => o.WorkflowRunId).ToListAsync(ct)) runs.Add(id);
            foreach (var owner in await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
                         .Where(b => b.OperationId == context.OwnerId).Select(b => new {b.OwnerKind, b.OwnerId}).ToListAsync(ct))
            {
                if (owner.OwnerKind == "workflow") Add(owner.OwnerId); else unknown = true;
            }
            foreach (var owner in await db.Set<ArtifactConsumerDbModel>().AsNoTracking()
                         .Where(c => c.OperationId == context.OwnerId).Select(c => new {c.OwnerKind, c.OwnerId}).ToListAsync(ct))
            {
                if (owner.OwnerKind == "workflow") Add(owner.OwnerId); else unknown = true;
            }
            // Old queued jobs precede consumer pinning; the durable lease still identifies them.
            var leaseOwner = await db.Set<ArtifactOperationDbModel>().AsNoTracking()
                .Where(o => o.Id == context.OwnerId).Select(o => o.LeaseOwner).SingleOrDefaultAsync(ct);
            if (leaseOwner?.StartsWith("workflow:", StringComparison.Ordinal) == true)
            {
                var end = leaseOwner.IndexOf(':', 9);
                if (end > 9) Add(leaseOwner[9..end]); else unknown = true;
            }
            else if (leaseOwner != null) unknown = true;
        }
        if (runs.Count == 0) return (owned, owned ? settings.MinimumFreeSpaceBytes : 0);
        var sources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking().Where(s => runs.Contains(s.WorkflowRunId) &&
            s.Module == "acquisition" && s.EntityKind == "task")
            .Select(s => new {s.WorkflowRunId, s.EntityId, s.Revision}).ToListAsync(ct);
        owned |= sources.Count > 0;
        foreach (var source in sources)
        {
            if (int.TryParse(source.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) taskIds.Add(id);
            else unknown = true;
        }
        var owners = await db.AcquisitionTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id) ||
            t.WorkflowRunId != null && runs.Contains(t.WorkflowRunId.Value))
            .Select(t => new AcquisitionTaskDbModel
            {
                Id = t.Id, WorkflowRunId = t.WorkflowRunId, PlanRevision = t.PlanRevision,
                DeletedAt = t.DeletedAt, Status = t.Status, BuilderStateJson = t.BuilderStateJson
            }).ToListAsync(ct);
        owned |= owners.Count > 0;
        if (!owned || unknown || owners.Count == 0 || taskIds.Any(id => owners.All(t => t.Id != id))) return (owned, owned ? settings.MinimumFreeSpaceBytes : 0);
        // These live checks run for every write. Materializing a file workflow's current item
        // would load megabytes of unrelated manifest text for each small file.
        var runRows = await db.WorkflowRuns.AsNoTracking().Where(r => runs.Contains(r.Id))
            .Select(r => new WorkflowRunDbModel {Id = r.Id, Status = r.Status}).ToDictionaryAsync(r => r.Id, ct);
        var now = DateTimeOffset.UtcNow;
        long minimum = 0;
        foreach (var runId in runs)
        {
            if (!runRows.TryGetValue(runId, out var run)) return (true, settings.MinimumFreeSpaceBytes);
            var runOwners = owners.Where(t => t.WorkflowRunId == runId).ToArray();
            if (runOwners.Length == 0) return (true, settings.MinimumFreeSpaceBytes);
            foreach (var owner in runOwners)
            {
                var grant = AcquisitionStorageOverrideGrant.Read(owner);
                // Expiring permission may revoke a lower reserve, but must never loosen
                // a run's explicit stricter selection back to a global emergency mode.
                var mode = grant is {ReserveMode: AcquisitionSpaceReserve.Configured} && grant.IsCurrentBinding(owner, run, now)
                    ? AcquisitionSpaceReserve.Configured : AcquisitionSpaceReserve.Inherit;
                if (grant?.IsValid(owner, run, now) == true)
                {
                    // A later retry cannot reuse a grant from a failed attempt.
                    var grantedAt = grant.GrantedAt.UtcDateTime;
                    if (!await db.Set<WorkflowNodeExecutionDbModel>().AsNoTracking().AnyAsync(n => n.WorkflowRunId == runId &&
                            (n.Status == "failed" || n.Status == "cancelled") && n.CompletedAt >= grantedAt, ct))
                        mode = grant.ReserveMode switch
                        {
                            AcquisitionSpaceReserve.Emergency => AcquisitionSpaceReserve.Emergency,
                            AcquisitionSpaceReserve.Inherit => AcquisitionSpaceReserve.Inherit,
                            _ => AcquisitionSpaceReserve.Configured
                        };
                }
                // Shared operations must satisfy the strictest of their current owners.
                minimum = Math.Max(minimum, AcquisitionSpaceReserve.Minimum(settings, mode));
                // A file action can start or adopt a run before its new run-bound grant is saved.
                // Polling a lost response may move the owning queue to running before adoption is reconciled.
                if (AcquisitionLocalRecoveryQueueEntry.Read(owner) is {UsesConfiguredReserve: true, Status: "starting" or "running"})
                    minimum = Math.Max(minimum, settings.MinimumFreeSpaceBytes);
            }
        }
        // Historical source/download owners whose current run changed remain protected too.
        if (owners.Any(t => t.WorkflowRunId == null || !runs.Contains(t.WorkflowRunId.Value))) return (true, settings.MinimumFreeSpaceBytes);
        foreach (var source in sources)
            if (!owners.Any(t => t.Id.ToString(CultureInfo.InvariantCulture) == source.EntityId && t.WorkflowRunId == source.WorkflowRunId &&
                    (source.Revision.Length == 0 || source.Revision == t.PlanRevision.ToString(CultureInfo.InvariantCulture)))) return (true, settings.MinimumFreeSpaceBytes);
        return (true, minimum);
    }
}
