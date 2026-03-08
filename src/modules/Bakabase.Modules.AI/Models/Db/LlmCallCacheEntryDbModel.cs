using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.AI.Models.Db;

public record LlmCallCacheEntryDbModel
{
    [Key]
    public long Id { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int ProviderConfigId { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ExpiresAt { get; set; }
    public int HitCount { get; set; }
}
