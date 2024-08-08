namespace Bakabase.Modules.Enhancer.Models.Input;

public record CategoryEnhancerTargetOptionsPatchInputModel
{
    public bool? IntegrateWithAlias { get; set; }
    public bool? AutoMatchMultilevelString { get; set; }
    public bool? AutoGenerateProperties { get; set; }
    public int? PropertyId { get; set; }
    public string? DynamicTarget { get; set; }
}