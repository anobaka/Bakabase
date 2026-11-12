using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    // Source parsers and plan builders supply content instructions, never deletion or storage policy.
    private static AcquisitionPlan WithoutProducerStoragePolicy(AcquisitionPlan plan) => plan with
    {
        StoragePolicy = null,
        Parts = plan.Parts.Select(part => part with
        {
            StoragePolicy = null, Processing = WithoutProducerStoragePolicy(part.Processing),
            Sources = part.Sources.Select(source => source with {Processing = WithoutProducerStoragePolicy(source.Processing)}).ToList()
        }).ToList()
    };
    private static FileProcessingPlan? WithoutProducerStoragePolicy(FileProcessingPlan? plan) => plan == null ? null :
        plan with {Steps = plan.Steps.Select(step => step with {StoragePolicy = null}).ToList()};

    private static AcquisitionPlan StoredStorageOverrides(AcquisitionPlan incoming, AcquisitionPlan? stored) => incoming with
    {
        Parts = incoming.Parts.Select(part =>
        {
            var previous = stored?.Parts.SingleOrDefault(p => p.Id == part.Id);
            FileProcessingPlan? Steps(FileProcessingPlan? value, FileProcessingPlan? old) => value == null ? null : value with
            {Steps = value.Steps.Select(step => step with {StoragePolicy = old?.Steps.SingleOrDefault(s => s.Id == step.Id)?.StoragePolicy}).ToList()};
            return part with
            {
                StoragePolicy = previous?.StoragePolicy,
                Processing = Steps(part.Processing, previous?.Processing ?? previous?.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId)?.Processing),
                Sources = part.Sources.Select(source => source with
                {Processing = Steps(source.Processing, previous?.Sources.SingleOrDefault(s => s.Id == source.Id)?.Processing)}).ToList()
            };
        }).ToList()
    };

    private ArtifactStoragePolicy CaptureStorage(ArtifactStoragePolicy? input = null)
    {
        var value = storageDefaults?.Capture(input) ?? input ?? new();
        ArtifactStoragePolicies.Validate(value);
        FileProcessingWorkspaceResolver.ValidateConfiguredRoot(value.ProcessingWorkspaceDirectory, storage);
        return value;
    }

    private async Task UpdateStoragePolicyAsync(AcquisitionTaskDbModel row, ArtifactStoragePolicy? input,
        IReadOnlyList<AcquisitionStoragePolicyOverrideInput>? overrides, CancellationToken ct)
    {
        if (input == null && overrides == null) return;
        if (!await HasNeverExecutedAsync(row, ct)) throw new InvalidOperationException("STORAGE_POLICY_ALREADY_STARTED");
        var policy = CaptureStorage(input ?? State(row).StoragePolicy);
        var plan = row.PlanJson == null ? null : Plan(row);
        if (overrides != null)
        {
            if (overrides.Count > 1000 || overrides.Any(p => p == null) ||
                overrides.Select(p => (p.PartId, p.StepId)).Distinct().Count() != overrides.Count)
                throw new ArgumentException("STORAGE_POLICY_OVERRIDE_INVALID");
            foreach (var patch in overrides)
            {
                var part = plan?.Parts.SingleOrDefault(p => p.Id == patch.PartId) ?? throw new ArgumentException("STORAGE_POLICY_PART_MISSING");
                if (patch.StepId == null) part = part with {StoragePolicy = patch.Policy};
                else
                {
                    if (patch.Policy?.DownloadDirectoryTemplate != null || patch.Policy?.SourceCleanup != null || patch.Policy?.ProcessingWorkspaceDirectory != null)
                        throw new ArgumentException("STORAGE_POLICY_STEP_FIELD_INVALID");
                    var selected = part.Sources.Single(s => s.Id == part.SelectedSourceId);
                    var processing = part.Processing ?? selected.Processing;
                    if (processing == null || processing.Steps.All(s => s.Id != patch.StepId)) throw new ArgumentException("STORAGE_POLICY_STEP_MISSING");
                    processing = processing with {Steps = processing.Steps.Select(s => s.Id == patch.StepId ? s with {StoragePolicy = patch.Policy} : s).ToList()};
                    part = part.Processing != null ? part with {Processing = processing} : part with
                    {Sources = part.Sources.Select(s => s.Id == selected.Id ? s with {Processing = processing} : s).ToList()};
                }
                plan = plan! with {Parts = plan.Parts.Select(p => p.Id == part.Id ? part : p).ToList()};
            }
        }
        if (plan != null)
        {
            ValidateStoragePaths(plan with {StoragePolicy = policy});
            row.PlanJson = Serialize(plan);
        }
        row.BuilderStateJson = Serialize(State(row) with {StoragePolicy = policy});
    }
    private void ValidateStoragePaths(AcquisitionPlan plan)
    {
        if (plan.StoragePolicy == null) return;
        _ = CaptureStorage(plan.StoragePolicy);
        foreach (var part in plan.Parts)
        {
            var policy = ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy);
            _ = CaptureStorage(policy);
            foreach (var processing in part.Sources.Select(s => s.Processing).Append(part.Processing).Where(p => p != null))
                foreach (var step in processing!.Steps)
                    _ = CaptureStorage(ArtifactStoragePolicies.Resolve(policy, step.StoragePolicy));
        }
    }
}
