using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// One resource inside one user-initiated move batch. The batch's BTask is the executor;
/// these rows are the durable record of truth (status, error, retry counters), so an
/// interrupted move is visible and retryable after a restart.
/// </summary>
public record ResourceMoveRecordDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>Groups the records created by one move request; also keys the executing BTask.</summary>
    [Required] public string BatchId { get; set; } = null!;

    public int ResourceId { get; set; }

    /// <summary>Standardized path of the resource when the batch was created.</summary>
    [Required] public string SourcePath { get; set; } = null!;

    /// <summary>Standardized full destination path (dest dir + source file/dir name).</summary>
    [Required] public string DestPath { get; set; } = null!;

    public ResourceMoveRecordStatus Status { get; set; }

    /// <summary>How many times execution started for this record (initial run included).</summary>
    public int Attempts { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
