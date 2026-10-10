using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

public partial class ResourceMoveService
{
    private sealed record SourcePlanResult(ResourceMoveExecutionPlan Plan, string? ReasonCode, int[] BlockingIds);

    private async Task<SourcePlanResult> BuildSourcePlan(int rootId, string sourcePath, string destPath,
        IReadOnlyDictionary<int, string> sourcePaths, CancellationToken cancellation = default, bool restoring = false)
    {
        var plan = new ResourceMoveExecutionPlan();
        var denied = new List<(int Id, string Code, bool Ancestor)>();
        var requestedExecutors = new HashSet<ResourceMoveExecutorReference>();
        var ids = sourcePaths.Keys.ToArray();
        // Do not use the public source-link service here: it intentionally filters unknown
        // source enums, which would turn an unsupported source into an unrestricted resource.
        var links = await db.ResourceSourceLinks.AsNoTracking().Where(l => Enumerable.Contains(ids, l.ResourceId))
            .OrderBy(l => l.ResourceId).ThenBy(l => l.Id).ToListAsync(cancellation);
        if (!restoring)
            denied.AddRange((await EvaluateProtectedBoundaries(rootId, sourcePath, destPath, ids, cancellation))
                .Select(d => (d.Id, d.Code, true)));
        foreach (var link in links)
        {
            if (!_sourceHandlers.TryGetValue(link.Source, out var handler))
            {
                denied.Add((link.ResourceId, "sourceMoveUnsupported", false));
                continue;
            }
            var oldPath = sourcePaths[link.ResourceId];
            var newPath = link.ResourceId == rootId ? destPath : destPath + oldPath[sourcePath.Length..];
            ResourceSourceMoveEvaluation evaluation;
            try
            {
                evaluation = await handler.EvaluateAsync(new(rootId, link.ResourceId, oldPath, newPath,
                    link.ToDomainModel(), IsSourceRestoration: restoring), cancellation);
            }
            catch (ResourceSourceMoveException e)
            {
                denied.Add((link.ResourceId, e.ReasonCode, false));
                continue;
            }
            if (evaluation.ReasonCode != null)
            {
                denied.Add((link.ResourceId, evaluation.ReasonCode, false));
                continue;
            }
            if (evaluation.Executor != null) requestedExecutors.Add(evaluation.Executor);
            plan.Sources.Add(new()
            {
                RootResourceId = rootId, ResourceId = link.ResourceId, LinkId = link.Id, Source = link.Source,
                SourceKey = link.SourceKey, HandlerVersion = handler.Version, SourcePath = oldPath, DestPath = newPath,
                PreviousLocation = evaluation.PreviousLocation, NewLocation = evaluation.NewLocation,
                StateJson = evaluation.StateJson
            });
        }
        if (requestedExecutors.Count > 1) denied.Add((rootId, "sourceMoveUnsupported", false));
        else if (requestedExecutors.SingleOrDefault() is { } selected)
        {
            plan.ExecutorId = selected.Id;
            plan.ExecutorVersion = selected.Version;
        }
        if (!_executors.TryGetValue(plan.ExecutorId, out var chosenExecutor) || chosenExecutor.Version != plan.ExecutorVersion)
            denied.Add((rootId, "sourceMoveUnsupported", false));
        if (denied.Count == 0) return new(plan, null, []);
        var steam = denied.Where(x => x.Code == "steamManaged").ToList();
        return steam.Count != 0
            ? new(plan, steam.Any(x => x.Id == rootId || x.Ancestor) ? "steamManaged" : "containsSteamManagedResource",
                steam.Select(x => x.Id).Distinct().ToArray())
            : new(plan, denied[0].Code, denied.Select(x => x.Id).Distinct().ToArray());
    }

    private async Task<List<(int Id, string Code)>> EvaluateProtectedBoundaries(int rootId, string sourcePath,
        string destPath, int[] ids, CancellationToken cancellation = default)
    {
        var denied = new List<(int Id, string Code)>();
        var protectedSources = _sourceHandlers.Values.Where(h => h.ProtectsLocalTree).Select(h => h.Source).ToArray();
        if (protectedSources.Length > 0)
        {
            var ancestors = (await db.ResourcesV2.AsNoTracking().ToListAsync(cancellation)).Where(r => !ids.Contains(r.Id) &&
                (sourcePath.IsPathEqualOrUnder(r.Path) || destPath.IsPathEqualOrUnder(r.Path))).ToDictionary(r => r.Id, r => r.Path!);
            var ancestorIds = ancestors.Keys.ToArray();
            var ancestorLinks = await db.ResourceSourceLinks.AsNoTracking().Where(l =>
                Enumerable.Contains(ancestorIds, l.ResourceId) && protectedSources.Contains(l.Source)).ToListAsync(cancellation);
            foreach (var link in ancestorLinks)
            {
                var evaluation = await _sourceHandlers[link.Source].EvaluateAsync(new(rootId, link.ResourceId,
                    ancestors[link.ResourceId], ancestors[link.ResourceId], link.ToDomainModel(), IsAncestorConstraint: true,
                    IsDestinationConstraint: destPath.IsPathEqualOrUnder(ancestors[link.ResourceId])), cancellation);
                if (evaluation.ReasonCode != null) denied.Add((link.ResourceId, evaluation.ReasonCode));
            }
        }
        return denied;
    }

    private IResourceMoveExecutor GetExecutor(ResourceMoveExecutionPlan plan)
    {
        if (plan.Version != 1 || !_executors.TryGetValue(plan.ExecutorId, out var executor) ||
            executor.Version != plan.ExecutorVersion)
            throw new ResourceSourceMoveException("sourceMoveUnsupported", "The recorded move executor version is unavailable");
        return executor;
    }

    private async Task<ResourceMoveExecutionPlan> GetExecutionPlan(ResourceMoveRecordDbModel record)
    {
        if (record.ExecutionPlanJson != null)
        {
            var saved = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(record.ExecutionPlanJson)
                ?? throw new ResourceSourceMoveException("sourceMoveUnsupported", "Invalid persisted move plan");
            GetExecutor(saved);
            return saved;
        }
        var snapshot = await GetSourceSnapshot(record);
        if (record.PhysicalMoveStarted)
        {
            // Old journaled moves had one known local executor, but no durable platform
            // location plan. Never infer an external platform's prior state after moving.
            var ids = snapshot.Keys.ToArray();
            if (await db.ResourceSourceLinks.AnyAsync(l => Enumerable.Contains(ids, l.ResourceId) && l.Source != ResourceSource.PathMark))
            {
                if (record.ConflictKind != "legacySourcePlanMissing") record.ConflictVersion++;
                record.ConflictKind = "legacySourcePlanMissing";
                record.ErrorCode = "legacySourcePlanMissing";
                record.Error = "This older move has no persisted source plan; manually restore the complete source and platform locations before confirming restoration";
                record.CanOverwrite = false;
                record.Status = ResourceMoveRecordStatus.NeedsRecovery;
                await db.SaveChangesAsync();
                throw new ResourceSourceMoveException("legacySourcePlanMissing", "This older move has no persisted source plan; manual recovery is required");
            }
        }
        var result = await BuildSourcePlan(record.ResourceId, record.SourcePath, record.DestPath, snapshot);
        if (result.ReasonCode != null) throw new ResourceSourceMoveException(result.ReasonCode);
        GetExecutor(result.Plan);
        record.ExecutionPlanJson = JsonConvert.SerializeObject(result.Plan);
        await db.SaveChangesAsync();
        return result.Plan;
    }

    private async Task ValidateSourcePlan(ResourceMoveRecordDbModel record, ResourceMoveExecutionPlan plan)
    {
        GetExecutor(plan);
        // The original plan remains authoritative once physical work starts. Handlers must
        // complete it idempotently; they may not choose a different policy/executor on retry.
        if (record.PhysicalMoveStarted)
        {
            await ValidateRecordedSourceState(record, plan);
            return;
        }
        var current = await BuildSourcePlan(record.ResourceId, record.SourcePath, record.DestPath,
            await GetSourceSnapshot(record));
        if (current.ReasonCode != null) throw new ResourceSourceMoveException(current.ReasonCode);
        if (JsonConvert.SerializeObject(plan) != JsonConvert.SerializeObject(current.Plan))
            throw new ResourceSourceMoveException("sourceLocationChanged", "Source links or platform locations changed; confirm a fresh preview");
    }

    private async Task ValidateRecordedSourceState(ResourceMoveRecordDbModel record, ResourceMoveExecutionPlan plan)
    {
        var ids = (await GetSourceSnapshot(record)).Keys.ToArray();
        var currentLinks = await db.ResourceSourceLinks.AsNoTracking().Where(l => Enumerable.Contains(ids, l.ResourceId)).ToListAsync();
        if (currentLinks.Count != plan.Sources.Count || plan.Sources.Any(step =>
                !currentLinks.Any(link => link.Id == step.LinkId && link.ResourceId == step.ResourceId &&
                    link.Source == step.Source && (link.SourceKey == step.SourceKey ||
                        step.Source == ResourceSource.PathMark && link.SourceKey == step.NewLocation))))
            throw new ResourceSourceMoveException("sourceLocationChanged", "Source links changed during the move; restore the recorded associations before recovery");
        var protectedBoundary = (await EvaluateProtectedBoundaries(record.ResourceId, record.SourcePath, record.DestPath, ids)).FirstOrDefault();
        if (protectedBoundary.Code != null) throw new ResourceSourceMoveException(protectedBoundary.Code);
        foreach (var step in plan.Sources)
        {
            if (!_sourceHandlers.TryGetValue(step.Source, out var handler) || handler.Version != step.HandlerVersion)
                throw new ResourceSourceMoveException("sourceMoveUnsupported", "The recorded source handler version is unavailable");
            await handler.ValidateRecordedStateAsync(step, CancellationToken.None);
        }
    }

    private async Task ApplySourcePlan(ResourceMoveRecordDbModel record, ResourceMoveExecutionPlan plan)
    {
        await ValidateRecordedSourceState(record, plan);
        // Reapplying already-checkpointed steps also validates that another operation has not
        // changed a platform location while a later participant was waiting for recovery.
        foreach (var step in plan.Sources)
        {
            await _sourceHandlers[step.Source].ApplyAsync(step, CancellationToken.None);
            step.Applied = true;
            record.ExecutionPlanJson = JsonConvert.SerializeObject(plan);
            await db.SaveChangesAsync();
        }
    }

    private static ResourceMoveExecutionState ExecutionState(ResourceMoveRecordDbModel record) => new()
    {
        Id = record.Id, SourcePath = record.SourcePath, DestPath = record.DestPath,
        MoveJournalJson = record.MoveJournalJson, PhysicalMoveStarted = record.PhysicalMoveStarted
    };
}
