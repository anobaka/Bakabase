using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record LlmProviderConfigDbModel
{
    [Key]
    public int Id { get; set; }
    public LlmProviderType ProviderType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
