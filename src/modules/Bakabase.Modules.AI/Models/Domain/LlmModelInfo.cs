namespace Bakabase.Modules.AI.Models.Domain;

public record LlmModelInfo
{
    public required string ModelId { get; init; }
    public required string DisplayName { get; init; }
    public LlmCapabilities Capabilities { get; init; }
}
