using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record EnhancementRecord
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int EnhancerId { get; set; }
    public DateTime? ContextCreatedAt { get; set; }
    public DateTime? ContextAppliedAt { get; set; }
    public EnhancementRecordStatus Status { get; set; }

    /// <summary>
    /// JSON serialized list of enhancement logs
    /// </summary>
    public string? Logs { get; set; }

    /// <summary>
    /// JSON serialized EnhancerFullOptions used during enhancement
    /// </summary>
    public string? OptionsSnapshot { get; set; }

    /// <summary>
    /// Error message if enhancement failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}