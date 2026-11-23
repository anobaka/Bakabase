using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>File capabilities own the meaning of their phases and measured archive progress.</summary>
public sealed class FileProcessingActivityRuntimeProvider(IEnumerable<ILlmFeatureExecutionPolicy> aiPolicies)
    : IWorkflowActivityRuntimeProvider
{
    private static readonly WorkflowCapabilityRuntime FileProcessing = new("fileProcessing", "File processing",
        "workflow.capability.fileProcessing");
    internal static WorkflowActivityRuntimeSnapshot ConfirmingExistingFiles { get; } = new()
    {
        Capability = FileProcessing with {Status = "running"}, Stage = "verifyingFiles", ProgressScope = "confirmation"
    };

    public bool CanHandle(string kind) => kind is "acquisition.plan.process" or "acquisition.plan.continueFiles" or
        "fs.processArtifacts" or "fs.publishArtifacts" or "fs.assessDelivery" or "download.processFiles" or
        "download.cleanupArtifacts" or "acquisition.unpack" or "acquisition.place" or "acquisition.materialize";

    public ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Project(context));
    }

    private WorkflowActivityRuntimeSnapshot Project(WorkflowActivityRuntimeContext context)
    {
        var live = context.CurrentProgress;
        var capability = FileProcessing;
        if (live?.Stage is "waitingForAi" or "analyzingFiles")
        {
            var policy = aiPolicies.FirstOrDefault(p => p.Feature == AiFeature.FileProcessor);
            capability = new(policy?.CapabilityId ?? "ai.fileProcessor", "File analysis", "workflow.capability.fileAnalysis",
                live.Stage == "waitingForAi" ? "queued" : "running", live.Stage == "waitingForAi" ? "capacity" : null,
                policy == null ? null : "bounded", policy?.Capacity);
        }
        var stage = live?.Stage is "extractingArchive" or "verifyingFiles" or "preparingFiles" or "processingFiles" or
            "checkingArchive" or "waitingForStorageCapacity" or "finalizingFiles" or "analyzingFiles" or "waitingForAi" ? live.Stage : null;
        return new()
        {
            Capability = capability,
            Stage = stage,
            Progress = stage == "extractingArchive" && live?.StageProgress is >= 0 and <= 100 ? live.StageProgress : null,
            ProgressScope = stage == "extractingArchive" ? "archive" : stage != null ? "processing" : null
        };
    }
}
