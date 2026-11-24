using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.Workflow;

/// <summary>One semantic boundary for provider diagnostics and generic execution attention tags.</summary>
public static class ExecutionProblemClassifier
{
    public static readonly string[] AttentionTags = ["sourceUnavailable", "sourceNeedsVerification", "accessRequired", "cloudStorageFull", "downloadQuotaExceeded", "localStorage", "networkIssue", "dependencyMissing", "downloadInfoMissing", "downloadSourceUnsupported", "sourceSelectionRequired", "configurationRequired", "fileProcessing", "other"];
    private sealed record Rule(string Status, string Tag, string? NormalizedReason = null);
    private static readonly Dictionary<string, Rule> Rules = CreateRules();
    public static bool IsKnown(string code) => Rules.ContainsKey(code);
    // A successful public listing cannot supersede a missing original-download URL.
    // That requires the provider's download recovery check, not a source-health probe.
    public static bool IsRefreshableSourceCode(string code) => code != "PIKPAK_ORIGINAL_FILE_UNAVAILABLE" && Rules.TryGetValue(code, out var rule) &&
        (rule.Tag is "sourceUnavailable" or "sourceNeedsVerification" or "networkIssue" || code is "SOURCE_CHECK_PASSED_RETRY_REQUIRED" or
            "BAIDU_SHARE_ACCESS_RESTRICTED" or "BAIDU_SHARE_VISITOR_LIMIT_REACHED" or "BAIDU_SHARE_FROZEN" or
            "SOURCE_SHARE_ACCESS_RESTRICTED" or "SOURCE_SHARE_VISITOR_LIMIT_REACHED" or "SOURCE_SHARE_FROZEN" or
            "BAIDU_ACCESS_CODE_REQUIRED" or "BAIDU_SHARE_ACCESS_CODE_REJECTED" or "BAIDU_SHARE_VERIFICATION_REQUIRED" or
            "SOURCE_ACCESS_CODE_REQUIRED" or "SOURCE_ACCESS_CODE_REJECTED" or "SOURCE_VERIFICATION_REQUIRED");
    public static string AvailabilityStatus(string? code) => code != null && Rules.TryGetValue(code, out var rule)
        ? rule.Status : SourceAvailabilityStatuses.Unknown;
    public static string? NormalizedReason(string? code) => code != null && Rules.TryGetValue(code, out var rule)
        ? rule.NormalizedReason ?? code : null;
    public static bool RequiresTransferTargetReview(string code) => code is
        "BAIDU_TRANSFER_FAILED_CONFIRM_TARGET" or "BAIDU_TRANSFER_INCOMPLETE_CONFIRM_TARGET";
    /// <summary>Capacity evidence is narrower than the local-storage attention category.
    /// Missing mounts, permissions and ambiguous write failures never imply a full disk.</summary>
    public static bool IsSpaceInsufficient(string? code) => NormalizedReason(code) is
        "LOCAL_STORAGE_FULL" or "LOCAL_STORAGE_RESERVE_LOW";
    public static IReadOnlyList<string> Classify(IEnumerable<string> codes, string? waitReason = null,
        AcquisitionWaitReason? legacyWaitReason = null)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
            if (Rules.TryGetValue(code, out var rule) && rule.Tag.Length > 0) tags.Add(rule.Tag);
        switch (waitReason)
        {
            case "cloud.fileProcessing": case "cloud.publicationConflict": case "cloud.publicationAttention":
            case "artifacts.publication":
                tags.Add("fileProcessing"); break;
            case "cloud.chooseSources": case "cloud.chooseMethod":
                tags.Add("sourceSelectionRequired"); break;
            case "cloud.chooseDirectory":
                tags.Add("configurationRequired"); break;
        }
        switch (legacyWaitReason)
        {
            case AcquisitionWaitReason.PaidContent: tags.Add("accessRequired"); break;
            case AcquisitionWaitReason.NoLinks: tags.Add("downloadInfoMissing"); break;
            case AcquisitionWaitReason.ChooseLink: tags.Add("sourceSelectionRequired"); break;
            case AcquisitionWaitReason.WaitingForFile: case AcquisitionWaitReason.AmbiguousInboxFile:
            case AcquisitionWaitReason.PasswordUnknown: case AcquisitionWaitReason.TargetExists:
            case AcquisitionWaitReason.PickDirectory: case AcquisitionWaitReason.ExtractionPlanUnknown:
                tags.Add("fileProcessing"); break;
        }
        if (tags.Count == 0) tags.Add("other");
        return AttentionTags.Where(tags.Contains).ToArray();
    }
    public static string Message(string? code) => AvailabilityStatus(code) switch
    {
        SourceAvailabilityStatuses.ConfirmedUnavailable => "The provider reported this source unavailable during an earlier attempt. Check again to confirm its current status.",
        SourceAvailabilityStatuses.AccessRequired => "Access information, account login or verification needs attention.",
        SourceAvailabilityStatuses.TemporarilyUnavailable => "A network or service problem interrupted this source; this does not prove the link expired.",
        _ when code == "SOURCE_CHECK_PASSED_RETRY_REQUIRED" => "The latest metadata check succeeded. Review the stopped step and continue using the existing task.",
        _ when code != null && Rules.TryGetValue(code, out var storage) && storage.Tag == "localStorage" =>
            (storage.NormalizedReason ?? code) switch
            {
                "LOCAL_STORAGE_FULL" => "The download or processing storage has insufficient free space. Free space, then diagnose and continue the original task using its retained files and checkpoints.",
                "LOCAL_STORAGE_RESERVE_LOW" => "The destination volume has reached the configured free-space reserve. Free space or adjust the acquisition reserve, then continue the original task using its retained files and checkpoints.",
                "LOCAL_STORAGE_ACCESS_DENIED" => "The download or processing directory denied access. Restore its permissions before continuing the original task.",
                "LOCAL_STORAGE_UNAVAILABLE" => "The original download or processing storage is unavailable. Restore the original mount before continuing; do not replace it with an empty directory.",
                _ => "The download or processing directory could not be written. Check free space, permissions and the original mount, then diagnose and continue. This error alone does not establish a full disk."
            },
        _ when code == "PasswordUnknown" => "The archive requires a password, or the supplied password was not accepted. Provide the correct password and continue processing.",
        _ when code == "ProcessingInputNotMatched" => "The processing step did not match any current input files. Review the selector or analyze the current files again; existing files are retained.",
        _ when code == "STAGE_COMMIT_REFERENCED" => "Another task or resource references the processing files. Review references before continuing from the existing files.",
        _ when code == "PLAN_CURRENT_DIRECTORY_CHANGED" => "The current input changed during this attempt. Reopen continuation to use the current files for a new attempt.",
        _ when code == "FILE_DELIVERY_RESULT_UNCERTAIN" => "Some processed files have an uncertain final role. All files were retained. Review the current files and confirm the final result or provide further processing instructions.",
        _ when code == "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED" => "The processed output still consists entirely of archives. Review the remaining files, add the next processing instructions, or explicitly keep these archives as the final result.",
        _ when code is "FILE_DELIVERY_INPUT_CHANGED" or "FILE_DELIVERY_REVIEW_CHANGED" or "FILE_DELIVERY_REVIEW_INVALID" => "The delivery-review input or answer has changed. Refresh the current files and review them again; no earlier answer will be applied to changed content.",
        _ when code == "FILE_CLEANUP_COVERAGE_INCOMPLETE" => "Some original files lack verified preservation or replacement evidence in the final output. The files were retained for review.",
        _ when code != null && Rules.TryGetValue(code, out var capacity) && capacity.Tag == "cloudStorageFull" => "Cloud storage is insufficient. Free space in the provider's official client, then diagnose and continue this task. Existing transfers must be checked before another save.",
        _ when code != null && Rules.TryGetValue(code, out var quota) && quota.Tag == "downloadQuotaExceeded" => "The provider reported that the current download account or free plan has insufficient transfer quota. Wait for the quota to reset or configure a supported account, then diagnose and continue.",
        _ when code != null && Rules.TryGetValue(code, out var rule) && rule.Tag == "sourceNeedsVerification" => "This source could not be verified. This does not establish that the link has expired.",
        _ => "This task needs attention. Open its execution details to review the current step."
    };

    private static Dictionary<string, Rule> CreateRules()
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        void Add(string tag, string status, params string[] codes)
        { foreach (var code in codes) rules.Add(code, new(status, tag)); }
        Add("sourceUnavailable", SourceAvailabilityStatuses.ConfirmedUnavailable,
            "BAIDU_SHARE_CANCELLED", "BAIDU_SHARE_EXPIRED", "SOURCE_CONFIRMED_UNAVAILABLE",
            "BAIDU_SHARE_DELETED", "BAIDU_SHARE_NOT_FOUND", "BAIDU_SHARE_BLOCKED", "BAIDU_SHARE_VISIT_LIMIT_INVALIDATED",
            "SOURCE_SHARE_CANCELLED", "SOURCE_SHARE_EXPIRED", "SOURCE_SHARE_NOT_FOUND", "SOURCE_FILE_NOT_FOUND", "SOURCE_NOT_FOUND",
            "GOFILE_CONTENT_NOT_FOUND", "PIKPAK_SHARE_EXPIRED", "TERABOX_SHARE_EXPIRED", "TERABOX_SHARE_DELETED");
        Add("sourceNeedsVerification", SourceAvailabilityStatuses.Unknown,
            "BAIDU_SHARE_UNAVAILABLE", "BAIDU_SHARE_PAGE_INVALID", "BAIDU_SHARE_RESPONSE_INVALID", "BAIDU_SHARE_VERIFICATION_FAILED",
            "PIKPAK_SHARE_UNAVAILABLE", "MEGA_SHARE_UNAVAILABLE", "GOFILE_SHARE_NOT_FOUND", "PIXELDRAIN_SHARE_UNAVAILABLE", "PIKPAK_ORIGINAL_FILE_UNAVAILABLE", "SOURCE_VALIDITY_UNKNOWN",
            "TERABOX_SHARE_UNAVAILABLE", "TERABOX_CLI_RESPONSE_INVALID", "TERABOX_MANIFEST_INVALID", "TERABOX_EMPTY_SHARE",
            "TERABOX_SHARE_CHANGED", "TERABOX_MANIFEST_CHANGED", "TERABOX_MANIFEST_LIMIT",
            "SOURCE_PROBE_PROTOCOL_CHANGED", "SOURCE_PROBE_SESSION_UNAVAILABLE", "SOURCE_PROBE_READONLY_TOKEN_REQUIRED", "PROBE_UNSUPPORTED", "PROBE_FAILED");
        Add("accessRequired", SourceAvailabilityStatuses.AccessRequired,
            "BAIDU_SHARE_ACCESS_RESTRICTED", "BAIDU_SHARE_VISITOR_LIMIT_REACHED", "BAIDU_SHARE_FROZEN",
            "BAIDU_ACCESS_CODE_REQUIRED", "BAIDU_SHARE_ACCESS_CODE_REJECTED", "BAIDU_SHARE_VERIFICATION_REQUIRED",
            "MEGA_ACCESS_REQUIRED", "PIKPAK_ACCOUNT_REQUIRED", "PIKPAK_ACCOUNT_CHANGED", "PIKPAK_PERMISSION_REQUIRED",
            "BAIDU_LOGIN_REQUIRED", "BAIDU_SESSION_EXPIRED", "BAIDU_ACCOUNT_UNAVAILABLE", "BAIDU_ACCOUNT_NOT_FOUND",
            "BAIDU_ACCOUNT_INVALID", "BAIDU_ACCOUNT_IDENTITY_CHANGED", "BAIDU_ACCOUNT_MISMATCH", "BAIDU_CREDENTIALS_INVALID",
            "CLOUD_ACCESS_REQUIRED", "GOFILE_ACCESS_REQUIRED", "PIKPAK_ACCESS_CODE_REQUIRED", "PIKPAK_PUBLIC_ACCESS_REQUIRED",
            "PIKPAK_VERIFICATION_REQUIRED", "SHAREPOINT_PUBLIC_ACCESS_REQUIRED", "RCLONE_ACCOUNT_REQUIRED",
            "RCLONE_ACCOUNT_NOT_CONFIGURED", "RCLONE_ACCOUNT_NOT_FOUND", "RCLONE_ACCOUNT_INVALID", "RCLONE_ACCOUNT_CHANGED",
            "RCLONE_CREDENTIAL_INVALID", "PIXELDRAIN_ACCESS_REQUIRED", "PIXELDRAIN_VERIFICATION_REQUIRED", "SITE_ACCOUNT_REQUIRED",
            "SOURCE_ACCESS_VERIFICATION_REQUIRED", "SOURCE_ACCESS_CODE_REQUIRED", "SOURCE_ACCESS_CODE_REJECTED",
            "SOURCE_VERIFICATION_REQUIRED", "SOURCE_AUTH_REQUIRED", "SOURCE_ACCESS_REQUIRED", "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED",
            "RCLONE_CONFIGURATION_ACCESS_DENIED", "CLOUD_ACCOUNT_REQUIRED", "TERABOX_ACCOUNT_REQUIRED", "TERABOX_ACCOUNT_CHANGED",
            "TERABOX_ACCESS_CODE_REQUIRED", "TERABOX_PERMISSION_REQUIRED",
            "TERABOX_DOWNLOAD_IDENTITY_REJECTED", "TERABOX_DOWNLOAD_ACCESS_DENIED");
        Add("networkIssue", SourceAvailabilityStatuses.TemporarilyUnavailable,
            "MEGA_NETWORK_ERROR", "MEGA_TRANSFER_QUOTA", "PIKPAK_NETWORK_UNAVAILABLE", "PIKPAK_TRANSFER_QUOTA_EXCEEDED",
            "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE",
            "BAIDU_SHARE_NETWORK_ERROR", "PUBLIC_SHARE_NETWORK_ERROR", "CLOUD_NETWORK_ERROR", "CLOUD_RATE_LIMITED", "PIXELDRAIN_NETWORK_ERROR", "PIXELDRAIN_RATE_LIMITED",
            "RCLONE_CONFIGURATION_NETWORK_FAILED", "RCLONE_CONFIGURATION_RATE_LIMITED", "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE",
            "SOURCE_NETWORK_ERROR", "SOURCE_RATE_LIMITED", "SOURCE_SERVICE_UNAVAILABLE", "SOURCE_RATE_OR_SERVICE_LIMIT",
            "TERABOX_NETWORK_UNAVAILABLE", "TERABOX_SIGNATURE_EXPIRED");
        Add("networkIssue", SourceAvailabilityStatuses.Unknown, "SOURCE_NETWORK_REQUIRES_PROXY", "BAIDU_SHARE_NETWORK_REQUIRES_PROXY");
        Add("dependencyMissing", SourceAvailabilityStatuses.Unknown,
            "DEPENDENCY_REQUIRED", "DEPENDENCY_UNAVAILABLE", "PLATFORM_UNSUPPORTED", "RCLONE_DEPENDENCY_REQUIRED",
            "PIKPAK_CLI_DEPENDENCY_REQUIRED", "MEGA_DEPENDENCY_REQUIRED", "MEGA_ISOLATED_RUNTIME_REQUIRED", "TERABOX_DEPENDENCY_REQUIRED");
        // Source support is independent of link health and tool/account setup. A known
        // integration limitation cannot be fixed by adding download or processing instructions.
        Add("downloadSourceUnsupported", SourceAvailabilityStatuses.Unknown,
            "SOURCE_UNSUPPORTED", "DOWNLOAD_METHOD_UNAVAILABLE", "NATIVE_DOCUMENT_EXPORT_UNSUPPORTED", "SHAREPOINT_FOLDER_REQUIRED",
            "WEIYUN_PUBLIC_SHARE_UNSUPPORTED");
        Add("downloadInfoMissing", SourceAvailabilityStatuses.Unknown,
            "SOURCE_INVALID", "SOURCE_URL_INVALID", "BUILDER_NO_RESULTS", "HANDOFF_NO_MAIN_SOURCE", "TERABOX_SHARE_INVALID", "TERABOX_SOURCE_INVALID",
            "MEGA_SHARE_INVALID");
        Add("sourceSelectionRequired", SourceAvailabilityStatuses.Unknown,
            "SOURCE_SELECTION_REQUIRED", "SELECTION_INVALID", "ONE_MIRROR_PER_GROUP", "SOURCE_DISABLED", "PLAN_REPLAN_SOURCE_CHANGED",
            "PIXELDRAIN_SHARE_REQUIRED", "PIXELDRAIN_DIRECTORY_REQUIRED");
        Add("configurationRequired", SourceAvailabilityStatuses.Unknown,
            "OUTPUT_DIRECTORY_REQUIRED", "TITLE_REQUIRED", "ONE_LIBRARY_TARGET", "WORKFLOW_INCOMPATIBLE", "ADAPTER_NOT_READY");
        Add("fileProcessing", SourceAvailabilityStatuses.Unknown,
            "PROCESSING_PLAN_INCOMPLETE", "PROCESSING_PLAN_INVALID", "PROCESSING_PLAN_REQUIRED", "PROCESSING_PLAN_UNKNOWN",
            "PROCESSING_CONFIRMATION_REQUIRED", "PROCESSING_STEPS_MISSING", "FILE_CLEANUP_COVERAGE_INCOMPLETE",
            "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED", "FILE_DELIVERY_RESULT_UNCERTAIN", "FILE_DELIVERY_INPUT_CHANGED", "FILE_DELIVERY_REVIEW_CHANGED", "FILE_DELIVERY_REVIEW_INVALID",
            "PasswordUnknown", "ExtractionPlanUnknown", "ProcessingInputNotMatched", "ArchiveInputRequired", "TargetExists", "MissingVolumes", "SourceChanged", "DestinationConflict",
            "STAGE_COMMIT_REFERENCED", "PLAN_CURRENT_DIRECTORY_CHANGED", "PLAN_CURRENT_DIRECTORY_UNAVAILABLE", "PLAN_CURRENT_DIRECTORY_EMPTY",
            "PLAN_CURRENT_DIRECTORY_TOO_LARGE", "PLAN_CURRENT_DIRECTORY_SHARED",
            "LLM_DECISION_INVALID_JSON", "LLM_DECISION_UNSUPPORTED_STATUS", "LLM_DECISION_INVALID",
            "LLM_OUTPUT_TRUNCATED", "LLM_ASSESSMENT_TIMEOUT", "LLM_ASSESSMENT_FAILED", "MAX_LLM_ASSESSMENT_CALLS",
            "INCOMPLETE_FILE_INVENTORY", "ARCHIVE_FAMILY_REVIEW_REQUIRED", "ARCHIVE_PACKAGING_REVIEW_REQUIRED");
        Add("cloudStorageFull", SourceAvailabilityStatuses.Unknown,
            "CLOUD_STORAGE_QUOTA_EXCEEDED", "PIKPAK_STORAGE_QUOTA_EXCEEDED");
        Add("downloadQuotaExceeded", SourceAvailabilityStatuses.Unknown,
            "SOURCE_DOWNLOAD_QUOTA_EXCEEDED", "PIXELDRAIN_TRANSFER_QUOTA_EXCEEDED", "PIXELDRAIN_DOWNLOAD_QUOTA_EXCEEDED");
        Add("localStorage", SourceAvailabilityStatuses.Unknown,
            "LOCAL_STORAGE_FULL", "LOCAL_STORAGE_WRITE_FAILED", "LOCAL_STORAGE_ACCESS_DENIED", "LOCAL_STORAGE_UNAVAILABLE", "LOCAL_STORAGE_RESERVE_LOW",
            "BT_LOCAL_FILE_MISSING", "BT_LOCAL_DISK_READ_ONLY", "BT_LOCAL_FILE_READ_FAILED", "BT_LOCAL_FILE_WRITE_FAILED",
            "BAIDU_OUTPUT_DIRECTORY_FAILED", "BAIDU_OUTPUT_FILE_INVALID", "BAIDU_LOCAL_PATH_TOO_LONG",
            "BAIDU_LOCAL_PATH_MAPPING_CONFLICT", "BAIDU_LOCAL_PATH_MAPPING_INVALID", "PUBLICATION_IO_ERROR", "RECOVERY_STORAGE_UNAVAILABLE", "StorageUnavailable", "DiskSpaceLimit");
        Add("other", SourceAvailabilityStatuses.Unknown, "RECOVERY_DOWNLOAD_REVIEW_REQUIRED", "RECOVERY_TRANSFER_REVIEW_REQUIRED",
            "BUILDER_FAILED", "BUILDER_SEARCH_FAILED", "SOURCE_CHECK_PASSED_RETRY_REQUIRED",
            "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED", "BAIDU_DOWNLOAD_CHECKPOINT_INVALID", "BAIDU_OUTPUT_UNEXPECTED_FILE",
            "RECOVERY_DOWNLOAD_HAS_OUTPUT", "RECOVERY_DOWNLOAD_IDENTITY_CHANGED", "RECOVERY_CHECK_INCOMPLETE",
            "PIKPAK_RESTORE_PENDING", "PIKPAK_RESTORE_REVIEW_REQUIRED", "PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED",
            "TERABOX_DOWNLOAD_REVIEW_REQUIRED", "TERABOX_CLI_REQUEST_FAILED");
        Add("", SourceAvailabilityStatuses.Available, "BAIDU_SHARE_METADATA_AVAILABLE", "TERABOX_SHARE_AVAILABLE");
        Add("dependencyMissing", SourceAvailabilityStatuses.Unsupported, "BAIDU_DEPENDENCY_REQUIRED", "BAIDU_UNKNOWN_COMMAND");
        void Normalize(string normalized, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var mapped = rules[alias] with {NormalizedReason = normalized};
                rules[alias] = mapped;
                rules[normalized] = mapped;
            }
        }
        Normalize("SOURCE_METADATA_AVAILABLE", "BAIDU_SHARE_METADATA_AVAILABLE", "TERABOX_SHARE_AVAILABLE");
        Normalize("SOURCE_SHARE_CANCELLED", "BAIDU_SHARE_CANCELLED");
        Normalize("SOURCE_SHARE_EXPIRED", "BAIDU_SHARE_EXPIRED", "TERABOX_SHARE_EXPIRED");
        Normalize("SOURCE_SHARE_DELETED", "BAIDU_SHARE_DELETED", "TERABOX_SHARE_DELETED");
        Normalize("SOURCE_SHARE_NOT_FOUND", "BAIDU_SHARE_NOT_FOUND");
        Normalize("SOURCE_SHARE_BLOCKED", "BAIDU_SHARE_BLOCKED");
        Normalize("SOURCE_SHARE_VISIT_LIMIT_INVALIDATED", "BAIDU_SHARE_VISIT_LIMIT_INVALIDATED");
        Normalize("SOURCE_SHARE_ACCESS_RESTRICTED", "BAIDU_SHARE_ACCESS_RESTRICTED");
        Normalize("SOURCE_SHARE_VISITOR_LIMIT_REACHED", "BAIDU_SHARE_VISITOR_LIMIT_REACHED");
        Normalize("SOURCE_SHARE_FROZEN", "BAIDU_SHARE_FROZEN");
        Normalize("SOURCE_SHARE_TEMPORARILY_UNAVAILABLE", "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE");
        Normalize("SOURCE_SHARE_NOT_FOUND", "GOFILE_CONTENT_NOT_FOUND", "PIKPAK_SHARE_EXPIRED");
        Normalize("SOURCE_ACCESS_CODE_REQUIRED", "BAIDU_ACCESS_CODE_REQUIRED", "PIKPAK_ACCESS_CODE_REQUIRED", "TERABOX_ACCESS_CODE_REQUIRED");
        Normalize("SOURCE_ACCESS_CODE_REJECTED", "BAIDU_SHARE_ACCESS_CODE_REJECTED");
        Normalize("SOURCE_VERIFICATION_REQUIRED", "BAIDU_SHARE_VERIFICATION_REQUIRED", "PIKPAK_VERIFICATION_REQUIRED", "PIXELDRAIN_VERIFICATION_REQUIRED");
        Normalize("SOURCE_ACCESS_REQUIRED", "MEGA_ACCESS_REQUIRED", "PIKPAK_ACCOUNT_REQUIRED", "PIKPAK_ACCOUNT_CHANGED", "PIKPAK_PERMISSION_REQUIRED",
            "BAIDU_LOGIN_REQUIRED", "BAIDU_SESSION_EXPIRED", "CLOUD_ACCESS_REQUIRED", "GOFILE_ACCESS_REQUIRED", "SOURCE_AUTH_REQUIRED",
            "PIKPAK_PUBLIC_ACCESS_REQUIRED", "SHAREPOINT_PUBLIC_ACCESS_REQUIRED", "RCLONE_ACCOUNT_REQUIRED", "RCLONE_ACCOUNT_NOT_CONFIGURED",
            "PIXELDRAIN_ACCESS_REQUIRED", "SITE_ACCOUNT_REQUIRED", "CLOUD_ACCOUNT_REQUIRED", "TERABOX_ACCOUNT_REQUIRED", "TERABOX_ACCOUNT_CHANGED", "TERABOX_PERMISSION_REQUIRED");
        Normalize("SOURCE_NETWORK_ERROR", "BAIDU_SHARE_NETWORK_ERROR", "PUBLIC_SHARE_NETWORK_ERROR", "CLOUD_NETWORK_ERROR", "PIXELDRAIN_NETWORK_ERROR", "RCLONE_CONFIGURATION_NETWORK_FAILED", "TERABOX_NETWORK_UNAVAILABLE");
        Normalize("SOURCE_RATE_OR_SERVICE_LIMIT", "CLOUD_RATE_LIMITED", "PIXELDRAIN_RATE_LIMITED", "RCLONE_CONFIGURATION_RATE_LIMITED", "SOURCE_RATE_LIMITED",
            "SOURCE_SERVICE_UNAVAILABLE", "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE");
        Normalize("SOURCE_DOWNLOAD_QUOTA_EXCEEDED", "PIXELDRAIN_TRANSFER_QUOTA_EXCEEDED", "PIXELDRAIN_DOWNLOAD_QUOTA_EXCEEDED");
        Normalize("SOURCE_NETWORK_REQUIRES_PROXY", "BAIDU_SHARE_NETWORK_REQUIRES_PROXY");
        Normalize("SOURCE_PROBE_DEPENDENCY_REQUIRED", "BAIDU_DEPENDENCY_REQUIRED");
        Normalize("SOURCE_PROBE_DEPENDENCY_UPDATE_REQUIRED", "BAIDU_UNKNOWN_COMMAND");
        // Old directory failures discarded the OS cause. They must not become evidence of ENOSPC.
        Normalize("LOCAL_STORAGE_WRITE_FAILED", "BAIDU_OUTPUT_DIRECTORY_FAILED", "PUBLICATION_IO_ERROR");
        Normalize("LOCAL_STORAGE_UNAVAILABLE", "RECOVERY_STORAGE_UNAVAILABLE", "StorageUnavailable");
        Normalize("LOCAL_STORAGE_FULL", "DiskSpaceLimit");
        // Ambiguous old NotFound/Unavailable codes intentionally have no sourceUnavailable rule.
        return rules;
    }
}
