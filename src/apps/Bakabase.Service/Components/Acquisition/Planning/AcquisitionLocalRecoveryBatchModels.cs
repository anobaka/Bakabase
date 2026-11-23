using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionLocalRecoveryBatchPreviewInput(IReadOnlyList<int> TaskIds, string Mode = "localOnly", string Order = "sizeAsc", int? MaxAdaptiveIterations = null);
public sealed record AcquisitionBatchActionSelection(int TaskId, string Action);
public sealed record AcquisitionLocalRecoveryBatchApplyInput(string PreviewToken, IReadOnlyList<int>? TaskIds = null,
    bool AllowStorageOverride = false, string? ReserveMode = null, bool? ConfirmTransferRetry = null,
    IReadOnlyList<AcquisitionBatchActionSelection>? Selections = null);
public sealed record AcquisitionLocalRecoveryBatchCapacityInput(string PreviewToken, IReadOnlyList<int>? TaskIds = null, string? ReserveMode = null,
    IReadOnlyList<AcquisitionBatchActionSelection>? Selections = null);
public sealed record AcquisitionLocalRecoveryBatchCapacityPreview(string PreviewToken, IReadOnlyList<int> TaskIds,
    IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> Volumes, bool RequiresSpaceConfirmation, bool EstimateIncomplete,
    IReadOnlyList<AcquisitionBatchActionSelection>? Selections = null);
public sealed record AcquisitionLocalRecoveryBatchPreviewRow(int TaskId, string? Title, int? WorkflowRunId,
    bool CanQueue, string ReasonCode, long? EstimatedBytes, bool EstimatePartial, string? RecoveryScope,
    string? WorkingDirectory, string? DeliveryDirectory, string? ActionCategory = null,
    AcquisitionRecoveryQueueReference? Queue = null, bool CanOverrideStorage = false,
    string? CurrentStepKind = null, string? RecoveryAction = null,
    IReadOnlyList<string>? AvailableActions = null, string? DefaultAction = null);
public sealed record AcquisitionRecoveryQueueReference(string Kind, string BatchId, string Status, string RowStatus, bool CanRequeue);
public sealed record AcquisitionLocalRecoveryBatchVolume(string VolumeId, IReadOnlyList<string> Paths,
    long AvailableBytes, long TotalBytes, long MinimumFreeBytes, long UsableBytes,
    long? EstimatedAdditionalBytes, bool EstimateIncomplete, bool ExceedsUsable)
{
    // The full-workflow estimate remains visible. Only current writes can block a proven checkpoint retry.
    public bool? CurrentWritesExceedUsable { get; init; }
    internal bool BlocksAdmission => CurrentWritesExceedUsable ?? ExceedsUsable;
}
public sealed record AcquisitionLocalRecoveryBatchPreview(string PreviewToken, DateTimeOffset ExpiresAt,
    IReadOnlyList<AcquisitionLocalRecoveryBatchPreviewRow> Rows, IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> Volumes,
    bool RequiresSpaceConfirmation, bool EstimateIncomplete, bool UseEmergencySpaceReserve = false, long ConfiguredMinimumFreeBytes = 0);
public sealed record AcquisitionLocalRecoveryBatchRow(int TaskId, string? Title, int Order, string Status,
    int? WorkflowRunId, long? EstimatedBytes, string? ReasonCode);
public sealed record AcquisitionLocalRecoveryBatch(string BatchId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string Status, bool AllowStorageOverride, DateTimeOffset? OverrideExpiresAt,
    IReadOnlyList<AcquisitionLocalRecoveryBatchRow> Rows, int? CurrentTaskId);

/// <summary>Small, typed durable scheduling intent. File/operation history remains in its original stores.</summary>
internal sealed record AcquisitionLocalRecoveryQueueEntry
{
    public required string BatchId { get; init; }
    public required string Version { get; init; }
    public required string Signal { get; init; }
    public required string RecoveryScope { get; init; }
    public string Mode { get; init; } = "localOnly";
    public string ActionCategory { get; init; } = "localProcessing";
    public bool VerificationPending { get; init; }
    public bool TransferRetryConfirmed { get; init; }
    public string SelectedAction { get; init; } = "continue";
    public int MaxAdaptiveIterations { get; init; } = 3;
    public string? AuthorizationStamp { get; init; }
    public bool Restricted { get; init; }
    public required string StorageStamp { get; init; }
    public IReadOnlyList<string> WorkingDirectories { get; init; } = [];
    public IReadOnlyList<string> DeliveryDirectories { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Order { get; init; }
    public string Status { get; init; } = "queued";
    public string? ReasonCode { get; init; }
    public int? WorkflowRunId { get; init; }
    public int PlanRevision { get; init; }
    public bool AllowStorageOverride { get; init; }
    public bool CanOverrideStorage { get; init; }
    public string? ReserveMode { get; init; }
    public DateTimeOffset? OverrideExpiresAt { get; init; }
    public long? EstimatedBytes { get; init; }
    public bool EstimateIncomplete { get; init; }
    public AcquisitionCheckpointWrite? CheckpointWrite { get; init; }
    public long MinimumFreeBytes { get; init; }
    public string? AttemptVersion { get; init; }
    public bool OverrideResumeAttempted { get; init; }

    internal static readonly string[] OpenStatuses = ["queued", "starting", "running", "paused", "spaceBlocked"];
    internal bool IsOpen => OpenStatuses.Contains(Status);
    internal bool IsParked => Status is "paused" or "spaceBlocked";
    internal AcquisitionLocalRecoveryQueueEntry Superseded() => this with {Status = "skipped", ReasonCode = "ACQUISITION_TASK_CHANGED"};
    internal bool OverrideValid(DateTimeOffset now) => (AllowStorageOverride || ReserveMode == "configured") && OverrideExpiresAt > now;
    internal bool UsesConfiguredReserve => ReserveMode == AcquisitionSpaceReserve.Configured && OverrideExpiresAt.HasValue;
    internal static AcquisitionLocalRecoveryQueueEntry? Read(AcquisitionTaskDbModel task)
    {
        try
        {
            var entry = JsonNode.Parse(task.BuilderStateJson ?? "{}")?["localRecoveryQueue"]?
                .Deserialize<AcquisitionLocalRecoveryQueueEntry>(WorkflowJson.Options);
            // Older replans could leave a parked approval attached even after a different run delivered.
            if (entry is {IsParked: true, PlanRevision: > 0, WorkflowRunId: > 0} &&
                task.Status == AcquisitionStatus.Completed && task.PlanRevision > entry.PlanRevision &&
                task.WorkflowRunId is > 0 && task.WorkflowRunId != entry.WorkflowRunId)
                return entry.Superseded();
            return entry;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }
    internal static string Write(string? json, AcquisitionLocalRecoveryQueueEntry entry)
    {
        var root = JsonNode.Parse(json ?? "{}") as JsonObject ?? throw new ArgumentException("ACQUISITION_CHECKPOINT_INVALID");
        if (root["localRecoveryQueue"] is { } previous && previous["batchId"]?.GetValue<string>() != entry.BatchId)
        {
            var history = root["localRecoveryQueueHistory"] as JsonArray ?? new JsonArray();
            if (history.Parent == null) root["localRecoveryQueueHistory"] = history;
            history.Add(previous.DeepClone());
        }
        root["localRecoveryQueue"] = JsonSerializer.SerializeToNode(entry, WorkflowJson.Options);
        return root.ToJsonString(WorkflowJson.Options);
    }
    // A selected alternative can replace a queued continuation and revoke the old batch's grant.
    // These are the only additional transitions made by RetireSelectedQueue before dispatch.
    internal static string ActionInputVersion(AcquisitionTaskDbModel task, WorkflowRunDbModel? run)
    {
        var state = JsonNode.Parse(task.BuilderStateJson ?? "{}") as JsonObject ?? new();
        if (AcquisitionContinuationEntry.Read(task) is {Status: "queued"} or
            {Status: "cancelled", ReasonCode: "LOCAL_BATCH_REQUEUED"})
            state.Remove("continuationRequest");
        var grant = AcquisitionStorageOverrideGrant.Read(task);
        if (grant is {Status: "revoked", ReasonCode: "LOCAL_BATCH_REQUEUED"} &&
            state["localRecoveryQueueHistory"] is JsonArray history && history.Any(item =>
                item?["status"]?.GetValue<string>() == "cancelled" &&
                item["reasonCode"]?.GetValue<string>() == "LOCAL_BATCH_REQUEUED" &&
                "batch:" + item["batchId"]?.GetValue<string>() == grant.Token &&
                item["workflowRunId"]?.GetValue<int>() == grant.WorkflowRunId &&
                item["planRevision"]?.GetValue<int>() == grant.PlanRevision))
            state.Remove("storageOverride");
        return ContentVersion(task with {BuilderStateJson = state.ToJsonString(WorkflowJson.Options)}, run);
    }

    // Queue bookkeeping and this queue's scoped grant must not invalidate its own frozen
    // content; all business fields, prior grants, plan and workflow checkpoints still do.
    internal static string ContentVersion(AcquisitionTaskDbModel task, WorkflowRunDbModel? run)
    {
        var state = JsonNode.Parse(task.BuilderStateJson ?? "{}") as JsonObject ?? new();
        var queue = Read(task);
        state.Remove("localRecoveryQueue");
        state.Remove("localRecoveryQueueHistory");
        if (queue != null && state["storageOverride"]?["token"]?.GetValue<string>() == "batch:" + queue.BatchId)
            state.Remove("storageOverride");
        return AcquisitionRecoveryService.Version(task with {BuilderStateJson = state.ToJsonString(WorkflowJson.Options)}, run);
    }
}

public sealed class AcquisitionLocalRecoveryBatchPreviewStore
{
    internal sealed record Evidence(string Version, string Signal, string Scope, string StorageStamp,
        IReadOnlyList<string> WorkingDirectories, IReadOnlyList<string> DeliveryDirectories, bool EstimateIncomplete,
        string Mode = "localOnly", string ActionCategory = "localProcessing", string? QueueStamp = null,
        bool VerificationPending = false, string? AuthorizationStamp = null, bool Restricted = false, bool CanOverrideStorage = false,
        bool RecheckCompletedPlacement = false, AcquisitionCheckpointWrite? CheckpointWrite = null);
    internal sealed record Entry(AcquisitionLocalRecoveryBatchPreview Preview,
        IReadOnlyDictionary<int, Evidence> Evidence, long MinimumFreeBytes)
    {
        internal string? BatchId;
        internal AcquisitionBatchActionSelection[]? Selected;
        internal IReadOnlyDictionary<(int TaskId, string Action), Evidence> Alternatives { get; init; } =
            new Dictionary<(int, string), Evidence>();
        internal int MaxAdaptiveIterations { get; init; } = 3;
        internal string? ReserveMode;
    }
    internal static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries.Where(p => p.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow))
            _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 50) throw new ArgumentException("LOCAL_BATCH_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewToken] = entry;
    }
    internal Entry Get(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var value) ||
            value.Preview.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("LOCAL_BATCH_PREVIEW_EXPIRED");
        return value;
    }
}
