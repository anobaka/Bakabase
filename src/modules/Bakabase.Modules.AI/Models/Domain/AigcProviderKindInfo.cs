namespace Bakabase.Modules.AI.Models.Domain;

public record AigcProviderKindInfo
{
    public required AigcProviderKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required AigcMediaType[] SupportedMediaTypes { get; init; }
    public required bool RequiresApiKey { get; init; }
    public required bool RequiresEndpoint { get; init; }
    public string? DefaultEndpoint { get; init; }
}
