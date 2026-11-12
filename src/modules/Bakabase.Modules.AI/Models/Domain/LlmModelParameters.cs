using System.Text.Json;

namespace Bakabase.Modules.AI.Models.Domain;

public record LlmModelParameters
{
    /// <summary>Optional in-process receipt for the exact provider call. Cache hits do not create a usage log.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public Action<long>? OnUsageRecorded { get; init; }
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public float? TopP { get; init; }
    /// <summary>
    /// When true, requests the LLM to return a JSON response via the API's response_format parameter.
    /// </summary>
    public bool UseJsonResponseFormat { get; init; }
    /// <summary>
    /// Optional backend-only response schema for recognized local Qwen JSON requests.
    /// Other providers retain their existing JSON mode.
    /// </summary>
    public JsonElement? JsonResponseSchema { get; init; }
}
