namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// AI module configuration. Populated from AiOptions at the application layer to avoid circular dependency.
/// </summary>
public record AiModuleOptions
{
    public int? DefaultProviderConfigId { get; set; }
    public string? DefaultModelId { get; set; }
    public LlmQuotaConfig? Quota { get; set; }
    public bool EnableCache { get; set; } = true;
    public int DefaultCacheTtlDays { get; set; } = 7;
    public bool AuditLogRequestContent { get; set; }
}
