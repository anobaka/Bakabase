using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record AigcGenerationRunDbModel
{
    [Key]
    public int Id { get; set; }

    public int GeneratorId { get; set; }
    public AigcGenerationStatus Status { get; set; } = AigcGenerationStatus.Pending;

    /// <summary>Resolved prompt actually sent (after template expansion).</summary>
    public string? Prompt { get; set; }
    public string? NegativePrompt { get; set; }

    /// <summary>Raw request payload sent to the provider, for debugging.</summary>
    public string? RequestPayload { get; set; }
    /// <summary>Raw response payload received from the provider, for debugging.</summary>
    public string? ResponsePayload { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}
