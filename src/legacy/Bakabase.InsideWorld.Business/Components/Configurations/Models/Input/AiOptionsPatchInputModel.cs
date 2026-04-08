using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class AiOptionsPatchInputModel
{
    public int? DefaultProviderConfigId { get; set; }
    public string? DefaultModelId { get; set; }
    public LlmQuotaConfig? Quota { get; set; }
    public bool? EnableCache { get; set; }
    public int? DefaultCacheTtlDays { get; set; }
    public bool? AuditLogRequestContent { get; set; }
}