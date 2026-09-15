using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Db;

/// <summary>
/// One attempt at obtaining one resource. Deliberately a thin index over the workflow run that does
/// the work: the run owns the cursor, the item and the history, and this row owns only what the
/// acquisitions page asks about — which resource, by what route, how it is going.
/// <para>
/// The duplication of the run's status is the point. "Show me everything I am currently getting"
/// must not become a join across the workflow tables, and the acquisitions page outlives the runs
/// it indexes.
/// </para>
/// </summary>
public record AcquisitionTaskDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>The resource being acquired. Always set — a task with no target is not a task.</summary>
    public int ResourceId { get; set; }

    /// <summary>Context only: which collection the user started this from, for statistics.</summary>
    public int? CollectionId { get; set; }

    public AcquisitionLeadKind LeadKind { get; set; }

    /// <summary>
    /// For a platform holding, <c>"{Source}:{SourceKey}"</c>; otherwise the shared link or document
    /// row, copied from the lead so the task still reads sensibly after the lead is deleted.
    /// </summary>
    [MaxLength(2048)]
    public string? LeadValue { get; set; }

    /// <summary>The stored lead this came from, when it came from one.</summary>
    public int? AcquisitionLeadId { get; set; }

    /// <summary>The workflow definition acting as the recipe.</summary>
    public int RecipeDefinitionId { get; set; }

    /// <summary>The run doing the work. Null only between creating the row and starting the run.</summary>
    public int? WorkflowRunId { get; set; }

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
}
