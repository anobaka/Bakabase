using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private static bool ExplicitlyKeepAll(AcquisitionPlanReplanInput input) =>
        input.Items.All(i => i.UseAdaptiveAssessment == false && i.Plan is {Requirement: "notRequired", Steps.Count: 0} &&
            i.ContinueFromArtifactId != null && i.ArtifactEvidenceIds.Count == 1 && i.ArtifactEvidenceIds[0] == i.ContinueFromArtifactId);

    private static void ValidateLocalContinuationRevision(AcquisitionPlan plan, AcquisitionPlanReplanInput input,
        bool requireProcessingChange)
    {
        if (input.DeliveryContentLayout != null)
        {
            FileArtifactDeliveryLayout.Validate(input.DeliveryContentLayout);
            if (input.Items.Any(i => i.Plan.Requirement != "notRequired" || i.Plan.Steps is not {Count: 0} ||
                i.ContinueFromArtifactId == null || i.ArtifactEvidenceIds.Count != 1 ||
                i.ArtifactEvidenceIds[0] != i.ContinueFromArtifactId ||
                i.ExpectedDeliveryProjectionFingerprint is not {Length: 64} fp || !fp.All(Uri.IsHexDigit)))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
        }
        try { foreach (var item in input.Items) FileProcessingPlanValidator.Validate(item.Plan); }
        catch (InvalidOperationException) { throw new ArgumentException("PLAN_REPLAN_INVALID"); }
        // A storage revision cannot acquire anything again as a side effect of changing
        // local placement. Every part explicitly retains a verified existing input.
        if (input.Items.Count != plan.Parts.Count ||
            !input.Items.Select(i => i.PartId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(plan.Parts.Select(p => p.Id)) ||
            input.Items.Any(i => i.ContinueFromArtifactId == null ||
                i.Plan.Requirement is not ("required" or "notRequired" or "unknown") ||
                i.Plan.Requirement == "required" && i.Plan.Steps.Count == 0 ||
                i.Plan.Requirement == "unknown" && i.Plan.Steps is not {Count: 0}) ||
            requireProcessingChange && input.DeliveryContentLayout == null && !ExplicitlyKeepAll(input) && !input.Items.Any(i => i.Plan.Requirement == "unknown" ||
                i.Plan.Requirement == "required" && i.Plan.Steps.Count > 0))
            throw new ArgumentException("PLAN_REPLAN_INVALID");
    }

    private async Task<AcquisitionPlan> PrepareLocalContinuationDeliveryAsync(AcquisitionTaskDbModel row,
        AcquisitionPlan plan, int previousRunId, bool useCurrentStorageDefaults, ArtifactDeliveryPolicy? contentLayout,
        Func<string?, Task>? guardDirectory, CancellationToken ct, AcquisitionLocalFilesStorage? reviewedStorage = null,
        string? deliveryRootOverride = null, bool preservePreviousDelivery = false)
    {
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        if (plan.Parts.Any(p => p.ContinueFrom == null) || compiled.Nodes.Any(n => n.Kind is not
                (AcquisitionPlanContinueFilesActivity.ActivityKind or AcquisitionPlanProcessingActivity.ActivityKind or
                    AssessFileArtifactDeliveryActivity.ActivityKind or "fs.publishArtifacts")))
            throw new ArgumentException("PLAN_REPLAN_INVALID");

        var state = State(row);
        var policy = state.StoragePolicy;
        if (plan.Parts.All(p => plan.StageDirectories?.Parts.GetValueOrDefault(p.Id)?.Delivery?.BindingId != null))
        {
            // The three-directory editor is the authority for new/explicit bindings.
            // Reprocessing is not an implicit request for an R-suffixed delivery location.
            if (deliveryRootOverride != null) throw new ArgumentException("PLAN_STAGE_DIRECTORY_EDIT_REQUIRED");
            if (contentLayout != null && plan.DeliveryLayout is { } boundLayout)
            {
                var revisedLayout = boundLayout with {Settings = boundLayout.Settings with {ContentLayout = contentLayout}};
                plan = plan with {DeliveryLayout = revisedLayout};
                row.BuilderStateJson = Serialize(state with {DeliveryLayout = revisedLayout,
                    DeliverySettings = state.DeliverySettings is {} settings ? settings with {ContentLayout = contentLayout} : null});
            }
            foreach (var part in plan.Parts)
            {
                var path = plan.StageDirectories!.Parts[part.Id].Delivery!.Directory;
                storage.EnsurePathAllowed(path); ArtifactManifestFiles.RejectLinks(path);
                if (guardDirectory != null) await guardDirectory(path);
            }
            return plan;
        }
        if (useCurrentStorageDefaults)
        {
            // This explicit opt-in affects only the local revision. Prior plans, directories
            // and receipts remain frozen, including task-specific placement overrides.
            var previous = policy ?? plan.StoragePolicy ?? new ArtifactStoragePolicy();
            policy = (reviewedStorage?.Policy ?? CaptureStorage()) with {SourceCleanup = previous.SourceCleanup,
                CleanupIntermediates = previous.CleanupIntermediates, Materialization = previous.Materialization};
            // Reuse only saved retention preferences. The review payload cannot introduce
            // deletion/materialization policy, and changing placement grants no deletion rights.
            plan = ResetContinuationPlacement(StoredStorageOverrides(plan, Plan(row)));
            if (guardDirectory != null) await guardDirectory(policy.ProcessingWorkspaceDirectory);
        }
        var old = useCurrentStorageDefaults
            ? DeliveryDirectories.Resolve(row.Id, plan.Title, plan.OutputDirectory, plan.Parts, reviewedStorage?.Delivery ?? DeliveryDirectories.Capture())
            : plan.DeliveryLayout ?? DeliveryDirectories.Resolve(row.Id, plan.Title,
                plan.OutputDirectory, plan.Parts, new AcquisitionDeliverySettings());
        if (deliveryRootOverride != null)
        {
            var requested = old.Settings with {RootDirectory = deliveryRootOverride};
            old = DeliveryDirectories.Resolve(row.Id, plan.Title, plan.OutputDirectory, plan.Parts, requested);
        }
        AcquisitionDeliveryDirectoryResolver.ValidateLayout(old, plan.Parts.Select(p => p.Id).ToArray());
        if (string.IsNullOrWhiteSpace(old.TaskDirectory)) throw new ArgumentException("PLAN_REPLAN_DELIVERY_CONFLICT");
        var nextDirectory = old.TaskDirectory + " [R" + checked(row.PlanRevision + 1).ToString(CultureInfo.InvariantCulture) + "]";
        // A new local revision gets safe publication defaults even when its older plan predates layout policies.
        var layout = old with {TaskDirectory = nextDirectory, Settings = old.Settings with {ContentLayout = contentLayout ?? old.Settings.ContentLayout ?? new()},
            PartDirectories = old.PartDirectories.ToDictionary(p => p.Key,
                p => Path.GetFullPath(Path.Combine(nextDirectory, Path.GetRelativePath(old.TaskDirectory, p.Value))), StringComparer.Ordinal)};
        AcquisitionDeliveryDirectoryResolver.ValidateLayout(layout, plan.Parts.Select(p => p.Id).ToArray());
        foreach (var path in layout.PartDirectories.Values.Append(nextDirectory))
        {
            storage.EnsurePathAllowed(path);
            ArtifactManifestFiles.RejectLinks(path);
            if (guardDirectory != null) await guardDirectory(path);
        }
        // No directory is created during review. An existing directory, even empty, belongs
        // to someone else until proven otherwise. The publisher also uses no-overwrite moves.
        if (File.Exists(nextDirectory) || Directory.Exists(nextDirectory))
            throw new InvalidOperationException("PLAN_REPLAN_DELIVERY_CONFLICT");

        var owner = previousRunId.ToString(CultureInfo.InvariantCulture);
        var slots = plan.Parts.Select(p => p.Id + ":artifact.publish").ToHashSet(StringComparer.Ordinal);
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner).ToListAsync(ct);
        var pending = new List<AcquisitionDeliveryRetirement>(state.PendingDeliveryRetirements ?? []);
        var retireAfterSuccess = !preservePreviousDelivery && deliveryRetirements?.Enabled == true;
        foreach (var binding in bindings.Where(b => slots.Contains(b.Slot)))
        {
            var operation = await artifacts!.GetOperationAsync(binding.OperationId, ct);
            var artifact = operation?.OutputArtifactId is { } id ? await artifacts.GetArtifactAsync(id, ct) : null;
            if (artifact != null && Overlap(nextDirectory, artifact.RootDirectory))
                throw new InvalidOperationException("PLAN_REPLAN_DELIVERY_CONFLICT");
            if (retireAfterSuccess && artifact?.Kind == "publishedFiles")
            {
                var partId = binding.Slot[..^":artifact.publish".Length];
                var retained = plan.Parts.Single(p => p.Id == partId).ContinueFrom;
                if (retained?.ArtifactId == artifact.Id && retained.PreviousRunId == previousRunId)
                    pending.Add(new(row.PlanRevision + 1, previousRunId, partId, artifact.Id));
            }
        }
        row.BuilderStateJson = Serialize(state with {DeliverySettings = layout.Settings, DeliveryLayout = layout, StoragePolicy = policy,
            PendingDeliveryRetirements = pending.Count == 0 ? null : pending});
        return plan;
    }

    private static AcquisitionPlan ResetContinuationPlacement(AcquisitionPlan plan)
    {
        static ArtifactStoragePolicyOverride? Placement(ArtifactStoragePolicyOverride? policy) => policy == null ? null :
            policy with {DownloadDirectoryTemplate = null, ProcessingDirectoryTemplate = null,
                ProcessingWorkspaceDirectory = null};
        static FileProcessingPlan? Steps(FileProcessingPlan? value) => value == null ? null : value with
            {Steps = value.Steps.Select(step => step with {StoragePolicy = Placement(step.StoragePolicy)}).ToList()};
        return plan with {Parts = plan.Parts.Select(part => part with {StoragePolicy = Placement(part.StoragePolicy),
            Processing = Steps(part.Processing), Sources = part.Sources.Select(source => source with
                {Processing = Steps(source.Processing)}).ToList()}).ToList()};
    }

    internal static bool Overlap(string left, string right)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
               a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
