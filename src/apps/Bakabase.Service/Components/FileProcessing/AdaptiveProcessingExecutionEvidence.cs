using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Advisory execution facts, never file-retention or publication authority.</summary>
internal sealed record AdaptiveCompletedProcessing(int Iteration, string InputArtifactId, string InputFingerprint,
    string OutputArtifactId, string OutputFingerprint, IReadOnlyList<AdaptiveCompletedOperation> Operations);
internal sealed record AdaptiveCompletedOperation(string Kind, int Count);
internal sealed record AdaptiveProcessingExecutionEvidence(string CurrentArtifactId, string CurrentFingerprint,
    IReadOnlyList<AdaptiveCompletedProcessing> CompletedProcessing)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Accept only an immutable result committed for this exact input. A proposed
    /// plan, failed attempt, partial checkpoint, or nested historical metadata is not proof.</summary>
    internal static AdaptiveCompletedProcessing? Capture(int iteration, ArtifactRecord input, ArtifactRecord output,
        ArtifactOperationDbModel? operation)
    {
        if (iteration is < 1 or > AdaptiveFileProcessingAssessmentService.MaxCallsPerTask ||
            output.Kind != "processedFiles" || output.State != ArtifactLocationState.Available ||
            output.ParentArtifactIds.Count != 1 || output.ParentArtifactIds[0] != input.Id ||
            operation is not {State: ArtifactOperationState.Completed, Kind: "files.process"} ||
            operation.Id != output.OperationId || operation.OutputArtifactId != output.Id ||
            operation.InputFingerprint != input.Fingerprint || output.MetadataJson == null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(output.MetadataJson);
            var metadata = document.RootElement;
            if (!metadata.TryGetProperty("inputArtifactId", out var source) || source.GetString() != input.Id ||
                metadata.TryGetProperty("completedThroughStepId", out var boundary) && boundary.ValueKind != JsonValueKind.Null ||
                !metadata.TryGetProperty("planJson", out var json) || json.ValueKind != JsonValueKind.String ||
                json.GetString() is not {Length: <= 131072} planJson)
                return null;
            var plan = FileProcessingPlanValidator.Validate(JsonSerializer.Deserialize<FileProcessingPlan>(planJson, Json));
            if (plan == null || plan.Steps.Count == 0) return null;
            // Do not copy selectors, paths, passwords, evidence, IDs, or user-authored strings
            // from the plan. The validated operation vocabulary is the only descriptive data.
            var actions = plan.Steps.GroupBy(step => step.Op)
                .Select(group => new AdaptiveCompletedOperation(group.Key, group.Count())).ToArray();
            return new(iteration, input.Id, input.Fingerprint, output.Id, output.Fingerprint, actions);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { return null; }
    }

    internal static AdaptiveProcessingExecutionEvidence? ForCurrent(ArtifactRecord current,
        IReadOnlyList<AdaptiveCompletedProcessing> completed)
    {
        if (completed.Count == 0 || completed.Count > AdaptiveFileProcessingAssessmentService.MaxCallsPerTask ||
            completed[^1].OutputArtifactId != current.Id || completed[^1].OutputFingerprint != current.Fingerprint)
            return null;
        for (var i = 1; i < completed.Count; i++)
            if (completed[i].InputArtifactId != completed[i - 1].OutputArtifactId ||
                completed[i].InputFingerprint != completed[i - 1].OutputFingerprint ||
                completed[i].Iteration <= completed[i - 1].Iteration)
                return null;
        return new(current.Id, current.Fingerprint, completed.ToArray());
    }
}
