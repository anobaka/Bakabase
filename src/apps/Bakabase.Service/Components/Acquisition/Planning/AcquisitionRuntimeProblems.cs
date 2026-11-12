using System;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only source-bound machine codes classify historical diagnostics. Raw logs never leave the service.</summary>
internal static class AcquisitionRuntimeProblems
{
    internal static string? Code(string? diagnostic) => diagnostic != null && ExecutionProblemClassifier.IsKnown(diagnostic)
        ? diagnostic : WorkflowSourceRunProblems.Extract(diagnostic).FirstOrDefault();
    // The runner captures partial item metadata before marking a failed invocation. Those
    // blocks can still describe the preceding wait, so only its explicit failure block and
    // current exception are failure evidence. Unknown failures must not revive an old wait.
    internal static string? CurrentFailureCode(WorkflowRunStatus? status, string? latestNodeStatus,
        string? error, System.Collections.Generic.IEnumerable<WorkflowOutputBlock> outputs)
    {
        if (status != WorkflowRunStatus.Failed || latestNodeStatus == "succeeded") return null;
        var explicitCode = latestNodeStatus == "failed" ? outputs.Where(o => o.Kind == "warning" &&
                o.Key == "activity-failed")
            .Select(o => Code(o.Code)).FirstOrDefault(c => c != null) : null;
        var exceptionCode = Code(error);
        // Persisted arbitrary I/O prose is not evidence of an OS space failure. Old typed
        // exception chains and bare application codes remain readable without raw output.
        if (exceptionCode?.StartsWith("LOCAL_STORAGE_", StringComparison.Ordinal) == true)
            exceptionCode = LocalStorageFailures.PersistedCode(error);
        return explicitCode ?? exceptionCode ?? "SOURCE_ATTENTION_REQUIRED";
    }
    // A typed prompt belongs to the current waiting cursor. A newer invocation must
    // never inherit its storage failure, and arbitrary prompt messages are not codes.
    internal static string? CurrentProcessingWaitCode(WorkflowRunStatus? status, string? activityKind,
        string? latestNodeStatus, string? promptJson)
    {
        if (status != WorkflowRunStatus.Waiting || activityKind is not (AcquisitionPlanProcessingActivity.ActivityKind or
                AssessFileArtifactDeliveryActivity.ActivityKind or "download.processFiles" or "fs.publishArtifacts") ||
            latestNodeStatus is not (null or "waiting") || string.IsNullOrWhiteSpace(promptJson) || promptJson.Length > 1024 * 1024)
            return null;
        try
        {
            using var document = JsonDocument.Parse(promptJson);
            var prompt = document.RootElement;
            if (prompt.ValueKind != JsonValueKind.Object || !prompt.TryGetProperty("reason", out var reason) ||
                reason.ValueKind != JsonValueKind.String) return null;
            var value = reason.GetString();
            if (value == "ProcessingPathConflict") return value;
            if (value is not ("StorageUnavailable" or "DiskSpaceLimit"))
                return value != null && ExecutionProblemClassifier.IsKnown(value) &&
                    ExecutionProblemClassifier.Classify([value]).Any(tag => tag is "fileProcessing" or "localStorage") ? value : null;
            var precise = prompt.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? WorkflowSourceRunProblems.Extract(message.GetString()).Select(ExecutionProblemClassifier.NormalizedReason)
                    .FirstOrDefault(code => code?.StartsWith("LOCAL_STORAGE_", StringComparison.Ordinal) == true)
                : null;
            return precise ?? ExecutionProblemClassifier.NormalizedReason(value);
        }
        catch (JsonException) { return null; }
    }
    internal static string? CurrentAssessmentDiagnostic(WorkflowRunStatus? status, string? activityKind,
        string? latestNodeStatus, System.Collections.Generic.IEnumerable<WorkflowOutputBlock> outputs)
    {
        if (status != WorkflowRunStatus.Waiting || latestNodeStatus != "waiting" ||
            activityKind != AssessFileArtifactDeliveryActivity.ActivityKind) return null;
        return outputs.Where(o => o.Kind == "warning" && o.Key == "adaptive-assessment")
            .Select(o => AdaptiveAssessmentDiagnostic.FromCode(o.Code)?.Code).FirstOrDefault(c => c != null);
    }
    internal static string Status(string? code) => ExecutionProblemClassifier.AvailabilityStatus(code);
    internal static string Message(string? code) => ExecutionProblemClassifier.Message(code);
}
