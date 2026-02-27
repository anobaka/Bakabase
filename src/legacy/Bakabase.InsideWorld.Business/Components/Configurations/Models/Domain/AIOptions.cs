using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "ai")]
public record AiOptions
{
    public string? OllamaEndpoint { get; set; }

    // AI integration settings
    public int? DefaultProviderConfigId { get; set; }
    public string? DefaultModelId { get; set; }
    public bool EnableCache { get; set; } = true;
    public int DefaultCacheTtlDays { get; set; } = 7;
}