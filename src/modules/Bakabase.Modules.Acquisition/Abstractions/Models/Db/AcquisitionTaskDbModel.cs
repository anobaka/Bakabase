using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Db;

/// <summary>
/// The stable business identity of one acquisition target. Legacy rows refer to a library resource
/// and recipe; prepared-plan rows own their title, preparation history and immutable plan revisions.
/// Workflow runs own execution checkpoints and node history; this row is their user-facing index.
/// </summary>
public record AcquisitionTaskDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>Optional library resource. Plan tasks own an independent title and may deliver files without importing.</summary>
    public int? ResourceId { get; set; }

    /// <summary>Context only: which collection the user started this from, for statistics.</summary>
    public int? CollectionId { get; set; }

    public string? Title { get; set; }
    public string? BuilderId { get; set; }
    public string? Phase { get; set; }
    public int PlanRevision { get; set; }

    public AcquisitionLeadKind LeadKind { get; set; }

    /// <summary>
    /// For a platform holding, <c>"{Source}:{SourceKey}"</c>; otherwise the shared link or document
    /// row, copied from the lead so the task still reads sensibly after the lead is deleted.
    /// </summary>
    [MaxLength(2048)]
    public string? LeadValue { get; set; }

    /// <summary>The stored lead this came from, when it came from one.</summary>
    public int? AcquisitionLeadId { get; set; }

    /// <summary>The legacy workflow template, absent for dynamically compiled plans.</summary>
    public int? RecipeDefinitionId { get; set; }

    /// <summary>The current execution, absent while a target is being prepared or awaits confirmation.</summary>
    public int? WorkflowRunId { get; set; }

    /// <summary>Server-prepared source selection and provenance, retained while queued and on retry.</summary>
    public string? PreparedContextJson { get; set; }

    public string? SourceRefsJson { get; set; }
    public string? PlanJson { get; set; }
    public string? BuilderStateJson { get; set; }
    public string? PlanHistoryJson { get; set; }

    public AcquisitionStatus Status { get; set; }

    /// <summary>Set while <see cref="Status"/> is <see cref="AcquisitionStatus.Waiting"/>.</summary>
    public AcquisitionWaitReason? WaitReason { get; set; }

    /// <summary>Where the files ended up, once placement has run.</summary>
    [MaxLength(1024)]
    public string? TargetDirectory { get; set; }

    /// <summary>Serialized <c>List&lt;PurchaseRecord&gt;</c> — money spent getting this.</summary>
    public string? PurchaseRecordJson { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>UTC tombstone. The task identity, source deduplication and execution history are retained.</summary>
    public DateTime? DeletedAt { get; set; }
    /// <summary>Null means this scope predates persistent cleanup receipts.</summary>
    public string? DeletedIntermediateCleanup { get; set; }
    public string? DeletedDeliveryCleanup { get; set; }
    /// <summary>Only machine reason codes from the latest cleanup attempt. Contains no file paths.</summary>
    public string? DeletedCleanupReasons { get; set; }
}
