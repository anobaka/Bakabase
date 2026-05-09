using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Input;

public record AiProviderAddInputModel
{
    public required AiProviderKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool LlmEnabled { get; init; }
    public bool AigcEnabled { get; init; }
    public string? AigcConfigJson { get; init; }
}

public record AiProviderUpdateInputModel
{
    public AiProviderKind? Kind { get; init; }
    public string? Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }

    public bool? IsEnabled { get; init; }
    public bool? LlmEnabled { get; init; }
    public bool? AigcEnabled { get; init; }
    public string? AigcConfigJson { get; init; }
}

public record AiProviderTestResult
{
    public bool? Llm { get; init; }
    public bool? Aigc { get; init; }
    public string? LlmMessage { get; init; }
    public string? AigcMessage { get; init; }
}
