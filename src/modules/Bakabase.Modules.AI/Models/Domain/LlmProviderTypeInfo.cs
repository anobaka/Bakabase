namespace Bakabase.Modules.AI.Models.Domain;

public record LlmProviderTypeInfo
{
    public required LlmProviderType Type { get; init; }
    public required string DisplayName { get; init; }
    public LlmCapabilities DefaultCapabilities { get; init; }
    public bool RequiresApiKey { get; init; }
    public bool RequiresEndpoint { get; init; }
    public string? DefaultEndpoint { get; init; }
}
