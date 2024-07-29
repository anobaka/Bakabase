using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

/// <summary>
/// To be brief, we put all possible options into one model for now, even they may be not suitable for current target.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public record EnhancerTargetFullOptions() : EnhancerTargetOptions
{
    public bool? IntegrateWithAlias { get; set; }
    public bool? AutoMatchMultilevelString { get; set; }
    public bool? AutoGenerateProperties { get; set; }
    public int? PropertyId { get; set; }
}