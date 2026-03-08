namespace Bakabase.Modules.AI.Models.Domain;

public record LlmModelParameters
{
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public float? TopP { get; init; }
}
