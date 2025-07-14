using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "ai")]
public record AiOptions
{
    public string? OllamaEndpoint { get; set; }
}