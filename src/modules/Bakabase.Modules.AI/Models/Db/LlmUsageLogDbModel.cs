using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record LlmUsageLogDbModel
{
    [Key]
    public long Id { get; set; }
    public int ProviderConfigId { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string? Feature { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public int DurationMs { get; set; }
    public bool CacheHit { get; set; }
    public LlmCallStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? RequestSummary { get; set; }
    public string? ResponseSummary { get; set; }
}
