using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Tasks;

public static class BTaskTelemetry
{
    // These loops poll even when there is no product work. Exclude all unattended outcomes,
    // including failures, so they cannot create a selectively sampled success-rate denominator.
    private static readonly HashSet<string> MaintenanceLoops =
    [
        "MetadataSync", "ResourceData", "Enhancement", "AcquisitionQueue", "SubscriptionCheck",
        "DataSync", "WorkflowRunCleanup", "SyncSteam", "SyncDLsite", "SyncExHentai",
        "ResourceProcessing", "DownloadResultDispatch", "PlatformFetchWatch", "WorkflowScheduler"
    ];

    public static bool ShouldCaptureAttempt(string id, bool automatically, bool recurring, bool hasActor) =>
        !(automatically && recurring && !hasActor && MaintenanceLoops.Contains(id));

    // IDs are used only to choose a fixed vocabulary. They never leave the process.
    public static string Feature(string id, BTaskType type) => id switch
    {
        _ when id.StartsWith("workflow.run.", StringComparison.Ordinal) => "workflow",
        "Enhancement" => "enhancer",
        "SearchIndex" => "search",
        "ResourceProfileIndex" => "resource_profiles",
        "PrepareCache" or "ResourceData" => "cache",
        "SubscriptionCheck" => "subscriptions",
        "AcquisitionQueue" => "acquisition",
        "SyncResources" => "library",
        "SyncPathMarks" => "path_marks",
        "MetadataSync" or "SyncSteam" or "SyncDLsite" or "SyncExHentai" => "sync",
        _ when id.StartsWith("DataSync", StringComparison.Ordinal) => "sync",
        "MoveFiles" => "file_management",
        _ when id.StartsWith("ResourceProcessing", StringComparison.Ordinal) => "resource_profiles",
        _ => type switch { BTaskType.Download => "download", BTaskType.Acquisition => "acquisition",
            BTaskType.MoveFiles or BTaskType.MoveResources or BTaskType.CopyFiles or BTaskType.Decompress => "file_management",
            _ => "unknown" }
    };

    public static string Outcome(BTaskStatus status, bool retryScheduled) => status switch
    {
        BTaskStatus.Completed => "success",
        BTaskStatus.Cancelled or BTaskStatus.Cancelling => "cancelled",
        BTaskStatus.WaitingForInput => "waiting",
        BTaskStatus.NotStarted when retryScheduled => "retry_scheduled",
        BTaskStatus.Error => "failed",
        _ => "unknown"
    };
}
