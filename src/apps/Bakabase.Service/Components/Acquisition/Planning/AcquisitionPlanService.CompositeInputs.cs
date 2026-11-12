using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private async Task<(AcquisitionPlan Plan, AcquisitionPlanReplanInput Input)> PrepareCompositeInputsAsync(
        AcquisitionTaskDbModel row, AcquisitionPlan plan, AcquisitionPlanReplanInput input,
        Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        if (input.CompositeInputs == null) return (plan, input);
        // The first composite surface deliberately requires all required parts. No donor can
        // silently disappear and no unrelated part can begin a fresh download after review.
        if (!input.UseCurrentStorageDefaults || input.DeliveryContentLayout != null || input.Items.Count != 1 ||
            input.CompositeInputs.Count is < 2 or > 10 || input.CompositeInputs.Count != plan.Parts.Count ||
            input.CompositeInputs.Any(i => i == null || string.IsNullOrWhiteSpace(i.PartId) || string.IsNullOrWhiteSpace(i.ArtifactId)) ||
            input.CompositeInputs.Select(i => i.PartId).Distinct().Count() != input.CompositeInputs.Count ||
            input.CompositeInputs.Select(i => i.ArtifactId).Distinct().Count() != input.CompositeInputs.Count ||
            !input.CompositeInputs.Select(i => i.PartId).ToHashSet().SetEquals(plan.Parts.Select(p => p.Id)) ||
            !input.Items[0].ArtifactEvidenceIds.ToHashSet().SetEquals(input.CompositeInputs.Select(i => i.ArtifactId)) ||
            input.Items[0].ContinueFromArtifactId != null || input.Items[0].ExpectedDeliveryProjectionFingerprint != null ||
            input.Items[0].Plan.Requirement != "required" || input.Items[0].Plan.Steps is not {Count: > 0})
            throw new ArgumentException("PLAN_COMPOSITE_INPUT_INVALID");
        FileProcessingPlanValidator.Validate(input.Items[0].Plan);
        var target = plan.Parts.SingleOrDefault(p => p.Id == input.Items[0].PartId)
            ?? throw new ArgumentException("PLAN_COMPOSITE_INPUT_INVALID");
        var origins = new List<AcquisitionCompositeOrigin>();
        var records = new List<ArtifactRecord>();
        foreach (var source in input.CompositeInputs.OrderBy(i => i.PartId, StringComparer.Ordinal))
        {
            var artifact = await artifacts!.GetArtifactAsync(source.ArtifactId, ct)
                ?? throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            if (artifact.Kind == AcquisitionCompositeFiles.Kind) throw new ArgumentException("PLAN_COMPOSITE_INPUT_INVALID");
            var reference = new AcquisitionPlanArtifactInput(row.Id, input.ExpectedRunId, artifact.Id, artifact.OperationId, artifact.Fingerprint);
            artifact = await AcquisitionPlanArtifactInputGuard.RequireAsync(db, artifacts, row.Id, source.PartId, reference, ct);
            storage.EnsurePathAllowed(artifact.RootDirectory);
            if (guardDirectory != null) await guardDirectory(artifact.RootDirectory);
            foreach (var file in artifact.Files) storage.EnsurePathAllowed(file);
            if (!(await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)).Available)
                throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
            origins.Add(new(source.PartId, reference)); records.Add(artifact);
        }
        var provenance = new AcquisitionCompositeProvenance(1, row.Id, input.ExpectedRunId, target.Id, origins);
        var projected = AcquisitionCompositeFiles.Project(records, provenance);
        storage.EnsurePathAllowed(projected.Root);
        // The common root is a coordinate system for exact files only. Managed=false prevents
        // reclamation or relocation from treating Downloads (or any shared ancestor) as owned.
        var operation = ArtifactManifestFiles.Hash("acquisition.composite:" + projected.Proof.InputFingerprint);
        var composite = await artifacts!.AdoptAsync(new(operation, "acquisition.composite:" + operation,
                AcquisitionCompositeFiles.OperationKind, projected.Proof.InputFingerprint),
            new(projected.Root, ArtifactManifestFiles.ResolveFiles(projected.Root, projected.Manifest),
                AcquisitionCompositeFiles.Kind, false, records.Select(a => a.Id).ToArray(),
                JsonSerializer.Serialize(projected.Proof, Json), projected.Manifest.Fingerprint,
                ArtifactManifestFiles.ResolveDirectories(projected.Root, projected.Manifest)),
            "acquisition-composite:" + row.Id, ct);
        if (!await AcquisitionCompositeFiles.IsOwnedAsync(db, artifacts, composite, row.Id, input.ExpectedRunId, target.Id, ct))
            throw new InvalidOperationException("PLAN_REPLAN_EVIDENCE_UNAVAILABLE");
        var item = input.Items[0] with {ArtifactEvidenceIds = [composite.Id], ContinueFromArtifactId = composite.Id};
        return (plan with {Parts = [target], DeliveryLayout = null,
            StageDirectories = plan.StageDirectories is { } directories ? directories with
                {Parts = directories.Parts.Where(p => p.Key == target.Id).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)} : null},
            input with {Items = [item], CompositeInputs = null});
    }
}
