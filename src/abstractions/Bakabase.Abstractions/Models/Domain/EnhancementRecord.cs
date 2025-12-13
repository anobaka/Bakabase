using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record EnhancementRecord
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int EnhancerId { get; set; }
    public DateTime? ContextCreatedAt { get; set; }
    public DateTime? ContextAppliedAt { get; set; }
    public EnhancementRecordStatus Status { get; set; }

    /// <summary>
    /// Enhancement logs collected during the enhancement process
    /// </summary>
    public List<EnhancementLog>? Logs { get; set; }

    /// <summary>
    /// EnhancerFullOptions used during enhancement
    /// </summary>
    public EnhancerFullOptions? OptionsSnapshot { get; set; }

    /// <summary>
    /// Error message if enhancement failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}