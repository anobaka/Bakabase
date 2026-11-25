using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>One acquisition target, with optional library identity and its current preparation or execution state.</summary>
public record AcquisitionTask
{
    public int Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsDeleted => DeletedAt != null;
    public int? ResourceId { get; set; }
    /// <summary>Historical library identity; independent drafts do not belong to the resource lifecycle.</summary>
    public int? OriginResourceId { get; set; }
    /// <summary>Read-only creation provenance, separate from the task's editable download sources.</summary>
    public IReadOnlyList<AcquisitionCreationOrigin> CreationOrigins { get; set; } = [];
    public int? CollectionId { get; set; }

    public string? Title { get; set; }
    public string? BuilderId { get; set; }
    public string? Phase { get; set; }
    public int PlanRevision { get; set; }
    public AcquisitionLeadKind LeadKind { get; set; }
    public string? LeadValue { get; set; }
    public int? AcquisitionLeadId { get; set; }
    public int? RecipeDefinitionId { get; set; }
    public int? WorkflowRunId { get; set; }
    public AcquisitionStatus Status { get; set; }
    /// <summary>Observed activity of this run's owned downloads; never changes workflow status or authorizes continuation.</summary>
    public string? DownloadActivity { get; set; }
    /// <summary>User-facing lifecycle, independent of the execution engine's status enum.</summary>
    public string Stage => ResolveStage(Status, WorkflowRunId, BuilderId, Phase, TargetDirectory, DownloadActivity);

    public static string ResolveStage(AcquisitionStatus status, int? workflowRunId, string? builderId,
        string? phase, string? targetDirectory, string? downloadActivity) => status switch
    {
        AcquisitionStatus.Cancelled => "cancelled",
        AcquisitionStatus.Completed => "completed",
        AcquisitionStatus.Failed => "attention",
        AcquisitionStatus.Running => "running",
        AcquisitionStatus.Waiting when downloadActivity is "running" or "queued" or "waitingForCloudStorage" => "running",
        _ when workflowRunId != null => status is AcquisitionStatus.Pending or AcquisitionStatus.Running
            ? "running" : "attention",
        _ when builderId != null => phase == "attention" ? "attention" :
            phase == "ready" && !string.IsNullOrWhiteSpace(targetDirectory) ? "ready" : "preparing",
        AcquisitionStatus.Pending or AcquisitionStatus.Running => "running",
        _ => "attention"
    };
    public AcquisitionWaitReason? WaitReason { get; set; }
    public string? TargetDirectory { get; set; }
    public string? Error { get; set; }
    /// <summary>Read-only classifications of the current blocker; never persisted into task history.</summary>
    public IReadOnlyList<string> AttentionTags { get; set; } = [];
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime CreatedAt { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime UpdatedAt { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The associated run's StartedAt, recorded when execution is submitted and including queue
    /// time. Null when no run is associated or its record is unavailable; not the first node's start.
    /// </summary>
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? ExecutionStartedAt { get; set; }

    /// <summary>The associated run's completion time, independent of task-index updates.</summary>
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? ExecutionCompletedAt { get; set; }

    /// <summary>Money spent getting this, as the steps recorded it.</summary>
    public List<PurchaseRecord> Purchases { get; set; } = [];

    /// <summary>Filled in for the acquisitions page, which shows what is being got rather than an id.</summary>
    public string? ResourceName { get; set; }

    public string? RecipeName { get; set; }

    /// <summary>What the waiting step wants asked, straight from the run.</summary>
    public string? WaitPromptJson { get; set; }

    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? WaitingSince { get; set; }

    /// <summary>Which step of the recipe the run is on, for the progress column.</summary>
    public int? CurrentStepIndex { get; set; }

    /// <summary>
    /// A task is finished when nothing more will happen to it without the user asking. Waiting is
    /// emphatically not finished — something is expected of a person, and the page must keep saying so.
    /// </summary>
    public bool IsFinished => Status is AcquisitionStatus.Completed or AcquisitionStatus.Failed
        or AcquisitionStatus.Cancelled;
}

public static class AcquisitionTaskExtensions
{
    public static AcquisitionTask ToDomainModel(this AcquisitionTaskDbModel db) => new AcquisitionTask
    {
        Id = db.Id,
        DeletedAt = db.DeletedAt is {} deleted ? DateTime.SpecifyKind(deleted, DateTimeKind.Utc) : null,
        ResourceId = db.ResourceId,
        OriginResourceId = ReadOriginResourceId(db.BuilderStateJson),
        CollectionId = db.CollectionId,
        Title = db.Title, BuilderId = db.BuilderId, Phase = db.Phase, PlanRevision = db.PlanRevision,
        LeadKind = db.LeadKind,
        LeadValue = db.LeadValue,
        AcquisitionLeadId = db.AcquisitionLeadId,
        RecipeDefinitionId = db.RecipeDefinitionId,
        WorkflowRunId = db.WorkflowRunId,
        Status = db.Status,
        WaitReason = db.WaitReason,
        TargetDirectory = db.TargetDirectory,
        Error = db.Error,
        CreatedAt = db.CreatedAt,
        UpdatedAt = db.UpdatedAt,
        CompletedAt = db.CompletedAt,
        Purchases = DeserializePurchases(db.PurchaseRecordJson),
    }.NormalizeTimestampsToUtc();

    /// <summary>
    /// Acquisition and run rows are written with server-local DateTime.Now. SQLite drops Kind,
    /// so treat an unspecified value as server-local and expose its UTC instant. Keep this at
    /// the projection boundary: storage and other modules' date contracts are unchanged.
    /// </summary>
    public static AcquisitionTask NormalizeTimestampsToUtc(this AcquisitionTask task)
    {
        if (task.DeletedAt is {} deleted) task.DeletedAt = DateTime.SpecifyKind(deleted, DateTimeKind.Utc);
        task.CreatedAt = task.CreatedAt.ToUniversalTime();
        task.UpdatedAt = task.UpdatedAt.ToUniversalTime();
        task.CompletedAt = task.CompletedAt?.ToUniversalTime();
        task.ExecutionStartedAt = task.ExecutionStartedAt?.ToUniversalTime();
        task.ExecutionCompletedAt = task.ExecutionCompletedAt?.ToUniversalTime();
        task.WaitingSince = task.WaitingSince?.ToUniversalTime();
        return task;
    }

    private static List<PurchaseRecord> DeserializePurchases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<PurchaseRecord>>(json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            // A task whose spending record is unreadable is still a task worth showing.
            return [];
        }
    }

    public static int? ReadOriginResourceId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                document.RootElement.TryGetProperty("legacyResourceId", out var value) &&
                value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var id)
                ? id : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
