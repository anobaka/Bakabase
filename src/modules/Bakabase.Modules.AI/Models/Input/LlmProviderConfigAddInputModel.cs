using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Input;

public record LlmProviderConfigAddInputModel
{
    public required LlmProviderType ProviderType { get; init; }
    public required string Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public bool IsEnabled { get; init; } = true;
}
