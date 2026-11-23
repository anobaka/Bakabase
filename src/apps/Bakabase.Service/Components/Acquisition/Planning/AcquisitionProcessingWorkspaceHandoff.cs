using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>The task, its owning runs and its cleanup receipt remain locked while an independent
/// artifact mutation runs. After a handoff recheck current registrations and consumers before unlinking.</summary>
internal sealed class AcquisitionProcessingWorkspaceHandoff(BakabaseDbContext db, IAsyncDisposable fence,
    string[] protectedIds, Func<CancellationToken, Task> requireDirectoryReferences)
{
    private sealed record Location(string Id, string Fingerprint, string OperationId, bool Managed,
        string Root, bool Relative, ArtifactLocationState State, string? OriginalRoot, bool OriginalRelative);
    private List<Location> locations = [];
    private List<string> children = [];

    private Task<List<Location>> ReadLocationsAsync(CancellationToken ct) =>
        db.Artifacts.AsNoTracking().Where(a => protectedIds.Contains(a.Id)).OrderBy(a => a.Id)
            .Select(a => new Location(a.Id, a.Fingerprint, a.OperationId, a.Managed, a.RootPath, a.RootIsAppData,
                a.State, a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData)).ToListAsync(ct);

    private Task<List<string>> ReadChildrenAsync(CancellationToken ct) => db.ArtifactLineage.AsNoTracking()
        .Where(e => protectedIds.Contains(e.ParentArtifactId)).OrderBy(e => e.Id).Select(e => e.Id).ToListAsync(ct);

    internal async Task CaptureAsync(CancellationToken ct)
    {
        locations = await ReadLocationsAsync(ct);
        children = await ReadChildrenAsync(ct);
    }

    // The final handoff is before checking all expected slots for absence. Never admit another
    // mutation between that proof, database retirement and the durable completion receipt.
    internal Task RequireBeforeRetirementAsync(CancellationToken ct) => RequireUnchangedAsync(ct);

    internal async Task YieldAsync(CancellationToken ct)
    {
        if (fence is not IArtifactMaintenanceHandoff handoff || !await handoff.YieldToWaitingMutationsAsync(ct)) return;
        await RequireUnchangedAsync(ct);
    }

    private async Task RequireUnchangedAsync(CancellationToken ct)
    {
        if (!locations.SequenceEqual(await ReadLocationsAsync(ct)) || !children.SequenceEqual(await ReadChildrenAsync(ct)))
            throw new IOException("files_changed");
        try { await requireDirectoryReferences(ct); }
        catch (InvalidOperationException ex) when (ex.Message is "STAGE_DIRECTORY_TASK_ACTIVE" or
                    "STAGE_DIRECTORY_SHARED" or "STAGE_DIRECTORY_REFERENCED")
        { throw new ProcessingWorkspaceReferencedException(ex); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { throw new IOException("files_changed", ex); }
    }
}

internal sealed class ProcessingWorkspaceReferencedException(Exception cause) : IOException("artifact_busy", cause);
