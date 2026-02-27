using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Input;

public record LlmProviderConfigUpdateInputModel
{
    public LlmProviderType? ProviderType { get; init; }
    public string? Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public bool? IsEnabled { get; init; }
}
