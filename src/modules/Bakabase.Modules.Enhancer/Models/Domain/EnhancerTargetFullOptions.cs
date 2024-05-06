using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.Enhancer.Models.Domain;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public record EnhancerTargetFullOptions() : EnhancerTargetOptions
{
    public bool? IntegrateWithAlias { get; set; }
    public bool? AutoMatchMultilevelString { get; set; }
}