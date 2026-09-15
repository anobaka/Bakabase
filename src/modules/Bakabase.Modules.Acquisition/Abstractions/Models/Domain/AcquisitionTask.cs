using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>One attempt at obtaining one resource, as the rest of the app sees it.</summary>
public record AcquisitionTask
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int? CollectionId { get; set; }
    public AcquisitionLeadKind LeadKind { get; set; }
    public string? LeadValue { get; set; }
    public int? AcquisitionLeadId { get; set; }
    public int RecipeDefinitionId { get; set; }
    public int? WorkflowRunId { get; set; }
    public AcquisitionStatus Status { get; set; }
    public AcquisitionWaitReason? WaitReason { get; set; }
    public string? TargetDirectory { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Money spent getting this, as the steps recorded it.</summary>
    public List<PurchaseRecord> Purchases { get; set; } = [];

    /// <summary>Filled in for the acquisitions page, which shows what is being got rather than an id.</summary>
    public string? ResourceName { get; set; }

    public string? RecipeName { get; set; }

    /// <summary>What the waiting step wants asked, straight from the run.</summary>
    public string? WaitPromptJson { get; set; }

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
    public static AcquisitionTask ToDomainModel(this AcquisitionTaskDbModel db) => new()
    {
        Id = db.Id,
        ResourceId = db.ResourceId,
        CollectionId = db.CollectionId,
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
    };

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
}
