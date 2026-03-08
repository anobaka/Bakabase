using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record AiFeatureConfigDbModel
{
    [Key]
    public int Id { get; set; }
    public AiFeature Feature { get; set; }
    public bool UseDefault { get; set; }
    public int? ProviderConfigId { get; set; }
    public string? ModelId { get; set; }
    public float? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public float? TopP { get; set; }
}
