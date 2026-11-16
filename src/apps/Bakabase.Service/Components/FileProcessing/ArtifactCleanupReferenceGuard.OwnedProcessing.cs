using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed partial class ArtifactCleanupReferenceGuard
{
    private sealed record PrivateProcessingAllocation(int TaskId, IReadOnlySet<int> RunIds, ArtifactDirectoryScope Scope);

    // This grants an allocation role to two typed directory fields, never permission to
    // ignore a task's complete document. Actual inputs and unknown fields remain readers.
    private async Task<PrivateProcessingAllocation?> PrivateProcessingAllocationAsync(ArtifactRecord source,
        IReadOnlyList<string>? privatePaths, IReadOnlySet<(int TaskId, int RunId, int Revision)> ownership, CancellationToken ct)
    {
        if (workspaces == null || privatePaths is not {Count: > 0} ||
            source is not {Managed: true, Kind: "processedFiles", State: ArtifactLocationState.Available} ||
            source.OperationId.Length != 64 || !source.OperationId.All(Uri.IsHexDigit)) return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(source.MetadataJson ?? "null", JsonSerializerOptions.Web);
            if (metadata is not {CompletedThroughStepId: null, WorkspaceDirectoryScope: { } scope} ||
                scope.OwnerKind != "acquisition" || !int.TryParse(scope.OwnerId, out var taskId) || taskId <= 0) return null;
            FileProcessingWorkspaceResolver.ValidateDirectoryScope(scope);
            var operation = await db.ArtifactOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == source.OperationId, ct);
            if (operation is not {State: ArtifactOperationState.Completed, Kind: "files.process", Attempt: > 0} ||
                operation.OutputArtifactId != source.Id) return null;
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == source.OperationId).ToListAsync(ct);
            if (bindings.Count == 0 || bindings.Any(b => b.OwnerKind != "workflow" || !int.TryParse(b.OwnerId, out _))) return null;
            var runs = bindings.Select(b => int.Parse(b.OwnerId, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            if (runs.Any(run => ownership.Count(o => o.RunId == run && o.TaskId == taskId) != 1) ||
                await db.WorkflowRuns.CountAsync(r => runs.Contains(r.Id) && r.Status != WorkflowRunStatus.Pending &&
                    r.Status != WorkflowRunStatus.Running && r.Status != WorkflowRunStatus.Waiting, ct) != runs.Count) return null;
            var location = await workspaces.InspectExistingAsync("operations", operation.Id, operation.Attempt, ct);
            if (location == null || !ArtifactCleanupPathReferences.StrictlyUnder(location.Directory, scope.Directory) ||
                !ArtifactCleanupPathReferences.StrictlyUnder(source.RootDirectory, location.Directory) ||
                privatePaths.Any(p => !ArtifactCleanupPathReferences.StrictlyUnder(p, location.Directory)) ||
                !await workspaces.HasOwnedLocationAsync(location, ct)) return null;
            return new(taskId, runs, scope);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
}
