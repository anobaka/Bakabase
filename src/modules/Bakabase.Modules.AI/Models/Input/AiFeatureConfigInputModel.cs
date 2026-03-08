namespace Bakabase.Modules.AI.Models.Input;

public record AiFeatureConfigInputModel
{
    public bool UseDefault { get; init; }
    public int? ProviderConfigId { get; init; }
    public string? ModelId { get; init; }
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public float? TopP { get; init; }
}
