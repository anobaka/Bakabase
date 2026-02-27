namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class AiOptionsPatchInputModel
{
    public string? OllamaEndpoint { get; set; }
    public int? DefaultProviderConfigId { get; set; }
    public string? DefaultModelId { get; set; }
    public bool? EnableCache { get; set; }
    public int? DefaultCacheTtlDays { get; set; }
}