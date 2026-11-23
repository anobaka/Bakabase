using System;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Recovery admission is based on the saved wait, never on a diagnostic supplied by the caller.</summary>
internal static class FileProcessingResumeActions
{
    public const string InspectGroup = "inspectIncompleteArchiveGroup";
    public const string RetainGroup = "retainIncompleteArchiveGroup";
    internal const string LegacyGroupMissingVolumes = "The archive is incomplete or a required volume could not be found. Check all volume names and parts; this does not establish a wrong password.";
    internal static bool IsLegacyGroupProbeWait(string prompt)
    {
        using var doc = JsonDocument.Parse(prompt);
        return doc.RootElement.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() == LegacyGroupMissingVolumes;
    }

    internal static void ValidateGroup(WorkflowExecutionContext context, CloudDownloadTarget target,
        CloudFileProcessingSignal signal, string? knownStepId) =>
        ValidateGroup(context.ResumePromptJson, target.Id, target.ProcessingWaitReason, signal, knownStepId);

    internal static void ValidateGroup(string? savedPrompt, string targetId, string? reason,
        CloudFileProcessingSignal signal, string? knownStepId)
    {
        if (reason != "MissingVolumes" || signal.TargetId != targetId ||
            signal.StepId == null || signal.StepId != knownStepId || string.IsNullOrWhiteSpace(signal.ArtifactId) ||
            string.IsNullOrWhiteSpace(signal.Fingerprint) || string.IsNullOrWhiteSpace(savedPrompt) ||
            signal.Password != null || signal.ExtractionPlanJson != null || signal.ArchiveId != null || signal.UserContext != null ||
            signal.ArchivePolicy != null || signal.RecursiveOptions != null || signal.StorageRecovery != null)
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        using var doc = JsonDocument.Parse(savedPrompt);
        var p = doc.RootElement;
        bool Matches(string name, string value) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() == value;
        if (!Matches("targetId", targetId) || !Matches("artifactId", signal.ArtifactId) || !Matches("fingerprint", signal.Fingerprint) ||
            !Matches("knownStepId", knownStepId!) || !Matches("reason", "MissingVolumes"))
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        if (signal.Action == InspectGroup && signal.ArchiveGroupToken == null &&
            (!p.TryGetProperty("archiveGroupInspected", out var inspected) || inspected.ValueKind is JsonValueKind.False or JsonValueKind.Null))
            return;
        if (signal.Action == RetainGroup && signal.ArchiveGroupToken?.Length == 64 &&
            p.TryGetProperty("allowedActions", out var actions) && actions.ValueKind == JsonValueKind.Array &&
            System.Linq.Enumerable.Any(actions.EnumerateArray(), a => a.ValueKind == JsonValueKind.String && a.GetString() == RetainGroup) &&
            p.TryGetProperty("incompleteArchiveGroup", out var group) && group.ValueKind == JsonValueKind.Object &&
            group.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String &&
            token.GetString() == signal.ArchiveGroupToken) return;
        throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
    }

    public const string RetryCurrentStep = "retryCurrentStep";
    public const string TryPostPasswordEvidence = "tryPostPasswordEvidence";

    public static string NormalizeReason(string? reason, string? message) =>
        reason != "ExtractionPlanUnknown" ? reason ?? "ExtractionPlanUnknown" : message switch
        {
            "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan." or
            "Archive format discovery exceeded its time limit. Revise the plan." or
            "Archive format discovery exceeded its time limit. Select a known archive or revise the plan." =>
                ArchiveFormatProbeFailureCodes.TimedOut,
            // Older probes could miss a ZIP beyond a media prefix even when the frozen
            // extraction step explicitly selected that file. Retry only that saved step;
            // the current probe must independently confirm the container and its limits.
            "No supported archive was recognized inside the selected media-prefixed file. Revise the extraction step." =>
                ArchiveFormatProbeFailureCodes.UpgradeRequired,
            _ => reason
        };

    public static bool CanRetry(string? reason) => reason is ArchiveFormatProbeFailureCodes.TimedOut or
        ArchiveFormatProbeFailureCodes.ToolFailed or ArchiveFormatProbeFailureCodes.UpgradeRequired or ArchiveFormatProbeFailureCodes.PrefixLimit or
        "DEPENDENCY_REQUIRED" or "ArchiveStorageUnavailable";

    public static string[] Allowed(string? reason) => CanRetry(reason) ? [RetryCurrentStep] : [];

    public static void Validate(WorkflowExecutionContext context, CloudDownloadTarget target,
        CloudFileProcessingSignal signal, string? knownStepId = null, bool allowSourceEvidence = false)
    {
        if (signal.Action != null && target.ProcessingWaitReason == null)
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        Validate(context, target.Id, target.ProcessingWaitReason, signal, knownStepId, allowSourceEvidence);
    }

    public static void Validate(WorkflowExecutionContext context, string targetId, string? waitReason,
        CloudFileProcessingSignal signal, string? knownStepId = null, bool allowSourceEvidence = false)
    {
        if (signal.Action == null) return;
        var evidence = allowSourceEvidence && context.WorkflowDefinitionId == 0 && signal.Action == TryPostPasswordEvidence;
        if ((!evidence && signal.Action != RetryCurrentStep) || signal.TargetId != targetId ||
            signal.Password != null || signal.ExtractionPlanJson != null || (!evidence && signal.ArchiveId != null) || signal.UserContext != null ||
            signal.ArchivePolicy != null || signal.RecursiveOptions != null || signal.StorageRecovery != null)
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_ACTION_INVALID");
        if (string.IsNullOrWhiteSpace(context.ResumePromptJson))
            throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        try
        {
            using var document = JsonDocument.Parse(context.ResumePromptJson);
            var prompt = document.RootElement;
            if (prompt.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
            string? Read(string name) => prompt.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var reason = Read("reason");
            if (evidence)
            {
                if (waitReason != "PasswordUnknown" || reason != waitReason || Read("targetId") != targetId ||
                    Read("knownStepId") != knownStepId || Read("archiveId") != signal.ArchiveId ||
                    (prompt.TryGetProperty("sourcePasswordEvidenceChecked", out var checkedFlag) && checkedFlag.ValueKind != JsonValueKind.False))
                    throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
                return;
            }
            if (Read("targetId") != targetId || Read("knownStepId") != knownStepId ||
                (waitReason != null && waitReason != reason) || !CanRetry(NormalizeReason(reason, Read("message"))))
                throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
            if (prompt.TryGetProperty("allowedActions", out var actions) &&
                (actions.ValueKind != JsonValueKind.Array || !HasRetry(actions) &&
                    // Before seek-based compatibility reading existed, this exact typed
                    // wait was emitted with an empty action list. Recheck only the saved
                    // target/step under current validation; this grants no plan mutation.
                    !(reason == ArchiveFormatProbeFailureCodes.PrefixLimit && actions.GetArrayLength() == 0)))
                throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED");
        }
        catch (JsonException) { throw new ArgumentException("FILE_PROCESSING_RECOVERY_WAIT_CHANGED"); }
    }

    private static bool HasRetry(JsonElement actions)
    {
        foreach (var action in actions.EnumerateArray())
            if (action.ValueKind == JsonValueKind.String && action.GetString() == RetryCurrentStep) return true;
        return false;
    }
}
