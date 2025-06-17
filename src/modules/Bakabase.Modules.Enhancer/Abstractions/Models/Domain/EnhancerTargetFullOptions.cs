using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Newtonsoft.Json;

namespace Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

/// <summary>
/// To be brief, we put all possible options into one model for now, even they may be not suitable for current target.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public record EnhancerTargetFullOptions() : EnhancerTargetOptions
{
    [Obsolete] public bool? AutoBindProperty { get; set; }
    public CoverSelectOrder? CoverSelectOrder { get; set; }
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
}