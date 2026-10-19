using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Services;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

public partial class ResourceMoveService
{
    private async Task RestoreMovedPathMarkAvailability(IReadOnlyDictionary<int, string> destinations, ISet<int> changed)
    {
        var ids = destinations.Keys.ToArray();
        var absent = await db.ResourcesV2.AsNoTracking().Where(resource => ids.Contains(resource.Id) &&
                resource.Status == ResourceStatus.Absent &&
                db.ResourceSourceLinks.Any(link => link.ResourceId == resource.Id && link.Source == ResourceSource.PathMark) &&
                !db.ResourceSourceLinks.Any(link => link.ResourceId == resource.Id && link.Source != ResourceSource.PathMark))
            .ToListAsync();
        try
        {
            foreach (var resource in absent)
            {
                if (resource.Path != destinations[resource.Id]) continue;
                try
                {
                    var attributes = File.GetAttributes(resource.Path);
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (resource.IsFile == isDirectory) continue;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { continue; }
                var count = await db.ResourcesV2.Where(row => row.Id == resource.Id && row.Path == resource.Path &&
                        row.IsFile == resource.IsFile && row.Status == ResourceStatus.Absent &&
                        db.ResourceSourceLinks.Any(link => link.ResourceId == row.Id && link.Source == ResourceSource.PathMark) &&
                        !db.ResourceSourceLinks.Any(link => link.ResourceId == row.Id && link.Source != ResourceSource.PathMark))
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, ResourceStatus.Active)
                        .SetProperty(row => row.UpdateDt, DateTime.Now));
                if (count > 0) changed.Add(resource.Id);
            }
        }
        finally
        {
            if (changed.Count > 0)
            {
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>().ClearCache();
            }
        }
    }

    private async Task<Dictionary<int, long>> PersistPostMoveMarks(IReadOnlyCollection<PathMark> snapshots)
    {
        if (snapshots.Count == 0) return [];
        // A failed mark write must not leave tracked changes in the move context and
        // prevent its NeedsRecovery state/journal from being saved.
        await using var scope = scopeFactory.CreateAsyncScope();
        var markDb = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var requestedAt = DateTime.UtcNow;
        var revisions = new Dictionary<int, long>();
        try
        {
            await using (var transaction = await markDb.Database.BeginTransactionAsync())
            {
                foreach (var snapshot in snapshots)
                {
                    var changed = await markDb.PathMarks.Where(mark => mark.Id == snapshot.Id && !mark.IsDeleted &&
                            mark.SyncStatus != PathMarkSyncStatus.PendingDelete && mark.SyncStatus == snapshot.SyncStatus &&
                            mark.Type == snapshot.Type && mark.Path == snapshot.Path && mark.ConfigJson == snapshot.ConfigJson &&
                            mark.Priority == snapshot.Priority && mark.UpdatedAt == snapshot.UpdatedAt)
                        .ExecuteUpdateAsync(update => update.SetProperty(mark => mark.SyncStatus, PathMarkSyncStatus.Pending)
                            .SetProperty(mark => mark.SyncError, (string?)null).SetProperty(mark => mark.UpdatedAt, requestedAt));
                    // Record only the revision we actually wrote, never a later manual
                    // edit observed after an awaited status notification.
                    if (changed > 0) revisions[snapshot.Id] = requestedAt.Ticks;
                }
                await transaction.CommitAsync();
            }
        }
        finally
        {
            scope.ServiceProvider.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, PathMarkDbModel, int>>().ClearCache();
        }
        var ids = revisions.Keys.ToArray();
        var current = await markDb.PathMarks.AsNoTracking().Where(mark => ids.Contains(mark.Id)).ToListAsync();
        foreach (var mark in current)
            try { await scope.ServiceProvider.GetRequiredService<IHubContext<WebGuiHub, IWebGuiClient>>()
                .Clients.All.GetIncrementalData("PathMark", mark.ToDomainModel()); }
            catch (Exception error) { logger.LogWarning(error, "Could not publish post-move mark {MarkId}", mark.Id); }
        // Integer ticks avoid the application's display-oriented JSON date format,
        // which truncates sub-millisecond precision and cannot identify a revision.
        return revisions;
    }

    private async Task<bool> HasPendingPostMoveMarks(IEnumerable<ResourceMoveRecordDbModel> records)
    {
        var revisions = records.Where(record => record.Status == ResourceMoveRecordStatus.Succeeded && record.ExecutionPlanJson != null)
            .Select(record => JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(record.ExecutionPlanJson!)?.PendingPathMarkRevisions)
            .OfType<Dictionary<int, long>>().SelectMany(receipt => receipt).ToArray();
        if (revisions.Length == 0) return false;
        var ids = revisions.Select(revision => revision.Key).ToArray();
        var pending = await db.PathMarks.AsNoTracking().Where(mark => ids.Contains(mark.Id) && !mark.IsDeleted &&
            mark.SyncStatus == PathMarkSyncStatus.Pending).ToListAsync();
        return pending.Any(mark => revisions.Any(revision => revision.Key == mark.Id && revision.Value == mark.UpdatedAt.Ticks));
    }
}
