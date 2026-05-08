using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Input;

public record AigcProviderConfigAddInputModel
{
    public required AigcProviderKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? ConfigJson { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public record AigcProviderConfigUpdateInputModel
{
    public AigcProviderKind? Kind { get; init; }
    public string? Name { get; init; }
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string? ConfigJson { get; init; }
    public bool? IsEnabled { get; init; }
}
