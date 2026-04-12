namespace Bakabase.Modules.AI.Models.Domain;

public record LlmModelParameters
{
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public float? TopP { get; init; }
    /// <summary>
    /// When true, requests the LLM to return a JSON response via the API's response_format parameter.
    /// </summary>
    public bool UseJsonResponseFormat { get; init; }
}
