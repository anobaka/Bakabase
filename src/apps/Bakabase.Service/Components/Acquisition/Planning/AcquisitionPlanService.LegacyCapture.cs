using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    // Older captures could include pending SMB deletions which were never in the
    // original download. Preserve the old receipt, and freeze a fresh unmanaged
    // input only on Run after proving that the user's selected payload is identical.
    private async Task<ArtifactRecord?> ResolveLegacyCaptureAsync(AcquisitionTaskDbModel row, AcquisitionPlanPart part,
        ArtifactRecord capture, bool start, CancellationToken ct)
    {
        if (artifacts == null || row.WorkflowRunId != null || row.Status != AcquisitionStatus.Waiting || row.Phase != "ready" ||
            capture.MetadataJson == null || !capture.Manifest.Entries.Any(e => FileProcessingFiles.IsDarwinSmbDeleteMarker(Path.GetFileName(e.RelativePath))))
            return null;
        var receipt = JsonSerializer.Deserialize<AcquisitionCurrentDirectoryReceipt>(capture.MetadataJson, Json);
        if (receipt is not {Version: 1, PriorInput: not null, VerifiedSmbFileNames: false} || receipt.TaskId != row.Id || receipt.PartId != part.Id ||
            part.ContinueFrom?.PreviousRunId != receipt.PreviousRunId ||
            await db.Set<WorkflowRunSourceDbModel>().AnyAsync(s => s.Module == "acquisition" && s.EntityKind == "task" &&
                s.EntityId == row.Id.ToString() && s.Revision == row.PlanRevision.ToString(), ct)) return null;

        var origin = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, part.Id, receipt.PriorInput,
            ct, currentDirectorySource: true);
        if (origin.Kind != "download" || origin.State != ArtifactLocationState.Available || origin.Manifest.Entries.Count == 0 ||
            !ArtifactManifestFiles.PathComparer.Equals(origin.RootDirectory, capture.RootDirectory) ||
            !(capture.Manifest.Directories ?? []).ToHashSet(StringComparer.Ordinal).SetEquals(origin.Manifest.Directories ?? []))
            return null;
        var original = origin.Manifest.Entries.ToHashSet();
        var names = original.Select(e => e.RelativePath).ToHashSet(StringComparer.Ordinal);
        var saved = capture.Manifest.Entries.ToHashSet();
        if (!original.IsSubsetOf(saved) || saved.Except(original).Any(e => names.Contains(e.RelativePath) ||
                !FileProcessingFiles.IsDarwinSmbDeleteMarker(Path.GetFileName(e.RelativePath)))) return null;

        await AcquisitionCurrentDirectoryFiles.ValidateSelectionAsync(capture, workspaces, ct, origin);
        if (!start) return origin;
        var id = ArtifactManifestFiles.Hash("refresh-current-input:" + capture.Id + ":" + row.PlanRevision)[..32];
        ArtifactRecord refreshed;
        try
        {
            refreshed = await AcquisitionCurrentDirectoryFiles.CaptureAsync(db, artifacts, storage, workspaces,
                row.Id, row.PlanRevision + 1, receipt.PreviousRunId, part.Id, origin, null, ct, receipt.PriorInput.PreviousRunId, id, origin.Manifest);
        }
        catch (IOException error) { throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED", error); }
        if (!original.SetEquals(refreshed.Manifest.Entries) ||
            !(refreshed.Manifest.Directories ?? []).ToHashSet(StringComparer.Ordinal).SetEquals(origin.Manifest.Directories ?? []))
            throw new InvalidOperationException("PLAN_CURRENT_DIRECTORY_CHANGED");
        return refreshed;
    }
}
