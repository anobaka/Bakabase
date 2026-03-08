using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "ai")]
public record AiOptions
{
    public string? OllamaEndpoint { get; set; }

    // AI integration settings
    public int? DefaultProviderConfigId { get; set; }
    public string? DefaultModelId { get; set; }
    public LlmQuotaConfig? Quota { get; set; }
    public bool EnableCache { get; set; } = true;
    public int DefaultCacheTtlDays { get; set; } = 7;
    public bool AuditLogRequestContent { get; set; }
}